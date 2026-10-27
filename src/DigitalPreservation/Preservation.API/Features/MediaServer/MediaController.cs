using System.Text.Json;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Cors;
using IIIFSize = IIIF.Size;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.IIIF;

namespace Preservation.API.Features.MediaServer;

[Route("[controller]")]
[ApiController]
[EnableCors("AllowAll")]
public class MediaController(
    IMediator mediator,
    WorkspaceManagerFactory workspaceManagerFactory,
    ITokenService tokenService,
    ILogger<MediaController> logger
) : ControllerBase
{
    // The largest size ManifestBuilder asks for as a thumbnail; anything bigger is a canvas image.
    private const int ThumbnailSize = 100;

    private static readonly Lazy<byte[]> CanvasPlaceholder = new(() => MediaImages.GreyPng(1000, 800));
    private static readonly Lazy<byte[]> ThumbPlaceholder = new(() => MediaImages.GreyPng(100, 80));

    [AllowAnonymous]
    [RequireFeatureFlag("EnableIiifMediaEndpoints")]
    // Level-0 image service: only /full/{w,h}/0/default.jpg is supported, plus /info.json
    [HttpGet("{token}/{source}/{sourceId}/{type}/{**localPath}", Name = "GetMedia")]
    public async Task<IActionResult> GetMedia(
        [FromRoute] string token,
        [FromRoute] string source,
        [FromRoute] string sourceId,
        [FromRoute] string type,
        [FromRoute] string localPath)
    {
        if (source != "deposit")
            return BadRequest(new ProblemDetails { Title = "Unsupported source", Detail = "Only 'deposit' is supported." });

        if (!ValidateLocalPath(localPath))
            return Unauthorized();

        if (!tokenService.IsValidForDeposit(token, sourceId))
            return this.IiifSessionExpiredResult();

        var depositResult = await mediator.Send(new GetDeposit(sourceId));
        if (depositResult is not { Success: true, Value: not null })
            return NotFound();

        var deposit = depositResult.Value;
        var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit);
        var workingDirectoryResult = await workspaceManager.GetFileSystemWorkingDirectory(false);
        var workingDirectory = workingDirectoryResult.Value;

        if (workingDirectory == null)
            return NotFound();

        if (type == "placeholder")
        {
            Response.Headers.CacheControl = "public, max-age=86400, immutable";
            return ServePlaceholder(localPath);
        }

        // Detect BagIt layout from the working directory tree rather than relying on
        // workspaceManager.IsBagItLayout, which is only set after GetCombinedDirectory runs.
        var isBagIt = workingDirectory.Directories.Any(d => d.LocalPath == FolderNames.BagItData);
        var origin = FolderNames.GetFilesLocation(deposit.Files!, isBagIt);

        if (type == "imagesvc")
            return await ImageServiceRequest(workspaceManager, workingDirectory, origin, isBagIt, localPath);

        var file = ResolveDepositFile(workingDirectory, isBagIt, origin, localPath);
        if (file == null)
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.ETag = FileETag(localPath, file.Value.Item.Size);
        return await ProxyFileWithByteRangeSupport(workspaceManager, file.Value, HttpContext);
    }

    // The three request shapes of the level-0 image service, most specific first:
    // .../imagesvc/{path}/info.json, .../imagesvc/{path}/full/{w,h}/0/default.jpg, and the bare
    // .../imagesvc/{path}, which redirects to its info.json when the file exists.
    private async Task<IActionResult> ImageServiceRequest(
        WorkspaceManager workspaceManager,
        WorkingDirectory workingDirectory,
        Uri origin,
        bool isBagIt,
        string localPath)
    {
        var elements = localPath.Split('/');

        if (elements[^1] == "info.json")
        {
            // "info.json" with no file path in front of it is not a request for anything.
            if (localPath.Length <= "/info.json".Length)
                return NotFound();
            var realLocalPath = localPath[..^"/info.json".Length];
            var mediaItem = ResolveDepositFile(workingDirectory, isBagIt, origin, realLocalPath)?.Item;
            var imageBaseUrl = Request.GetDisplayUrl();
            imageBaseUrl = imageBaseUrl[..imageBaseUrl.LastIndexOf("/info.json", StringComparison.Ordinal)];
            Response.Headers.CacheControl = "private, max-age=600";
            return InfoJson(imageBaseUrl, mediaItem);
        }

        // /full/{w,h}/0/default.jpg — requires at least 4 segments after the file path
        if (elements.Length >= 4 && elements[^1] == "default.jpg" && elements[^2] == "0" && elements[^4] == "full")
        {
            var size = elements[^3];
            var imageApi = $"/full/{size}/0/default.jpg";
            // The image API suffix with no file path in front of it is not a request for anything.
            if (localPath.Length <= imageApi.Length)
                return NotFound();
            var realLocalPath = localPath[..^imageApi.Length];
            var file = ResolveDepositFile(workingDirectory, isBagIt, origin, realLocalPath);
            if (file == null)
                return NotFound();
            Response.Headers.CacheControl = "private, max-age=3600";
            Response.Headers.ETag = ImageETag(realLocalPath, size);
            return await ImageFromImageService(workspaceManager, file.Value, size);
        }

        // Bare imagesvc URL: the image service's base URI, which is the whole path. Redirect to its
        // info.json if it names a file. Any other image API shape (another format, rotation or
        // quality) is not a file in the tree, so it 404s - this is a level 0 service.
        if (ResolveDepositFile(workingDirectory, isBagIt, origin, localPath) != null)
        {
            // Relative redirect: avoids echoing the client-supplied Host header
            // and any query string back into the Location header (Sonar S5146).
            var infoJsonUrl = Request.PathBase.Add(Request.Path).Add("/info.json").Value!;
            if (Url.IsLocalUrl(infoJsonUrl))
                return Redirect(infoJsonUrl);
        }

        return NotFound();
    }

    // A file's bytes are reached one way only: the path must name a file in this deposit's tree,
    // exactly, and only then is the S3 URI built. Every branch that touches S3 goes through here -
    // the image-bytes branch once did not, and that was the traversal. ValidateLocalPath is the
    // defence in depth in front of this gate, not the gate.
    private readonly record struct DepositFile(WorkingFile Item, Uri FileUri);

    private static DepositFile? ResolveDepositFile(
        WorkingDirectory workingDirectory, bool isBagIt, Uri origin, string localPath)
    {
        var item = workingDirectory.FindFile(FolderNames.GetPathPrefix(isBagIt) + localPath);
        return item == null
            ? null
            : new DepositFile(item, new Uri(origin.ToString().TrimEnd('/') + "/" + localPath));
    }

    // Defence in depth in front of ResolveDepositFile: nothing that could move the path elsewhere
    // is allowed to be built into an S3 URI at all. Routing decodes the route value once, so the
    // shared rule (UriPathX) judges each segment after one more decode as well.
    internal static bool ValidateLocalPath(string localPath)
    {
        if (string.IsNullOrEmpty(localPath)) return false;
        return !localPath.Split('/').Any(segment =>
            string.IsNullOrEmpty(segment) || UriPathX.IsTraversalSegment(segment));
    }

    private static string FileETag(string localPath, long? size) =>
        $"W/\"{localPath.GetHashCode():x}-{size ?? 0}\"";

    private static string ImageETag(string localPath, string sizeParam) =>
        $"W/\"{localPath.GetHashCode():x}-{sizeParam.GetHashCode():x}\"";

    private static async Task<IActionResult> ProxyFileWithByteRangeSupport(
        WorkspaceManager workspaceManager, DepositFile file, HttpContext httpContext)
    {
        var (item, fileUri) = file;
        var contentType = item.ContentType ?? "application/octet-stream";

        httpContext.Response.Headers.AcceptRanges = "bytes";

        var rangeHeader = httpContext.Request.Headers.Range.ToString();
        if (!string.IsNullOrEmpty(rangeHeader)
            && Microsoft.Net.Http.Headers.RangeHeaderValue.TryParse(rangeHeader, out var parsedRange)
            && parsedRange.Ranges.Count == 1)
        {
            var r = parsedRange.Ranges.First();
            if (r.From.HasValue)
            {
                var from = r.From.Value;
                var to = r.To;
                var streamResult = await workspaceManager.GetRangedStream(fileUri, from, to);
                if (streamResult is not { Success: true, Value: not null })
                    return new NotFoundResult();

                var rangedLength = streamResult.Value.RangedContentLength;
                var actualTo    = to ?? (from + rangedLength - 1);
                // Use item.Size when available; derive from ranged response when to=null (open-ended range)
                var knownTotal  = item.Size ?? (to == null ? from + rangedLength : (long?)null);
                var totalPart   = knownTotal.HasValue ? knownTotal.Value.ToString() : "*";

                httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
                httpContext.Response.Headers.ContentRange = $"bytes {from}-{actualTo}/{totalPart}";
                httpContext.Response.ContentLength = actualTo - from + 1;
                return new FileStreamResult(streamResult.Value.Stream, contentType);
            }
        }

        var fullStreamResult = await workspaceManager.GetStream(fileUri);
        if (fullStreamResult is not { Success: true, Value.Item1: not null })
            return new NotFoundResult();
        if (item.Size.HasValue)
            httpContext.Response.ContentLength = item.Size.Value;
        return new FileStreamResult(fullStreamResult.Value.Item1, contentType);
    }

    private static IActionResult ServePlaceholder(string localPath) => Placeholder(localPath == "thumb.png");

    private static FileContentResult Placeholder(bool thumbnail) =>
        new(thumbnail ? ThumbPlaceholder.Value : CanvasPlaceholder.Value, "image/png");

    // Takes a resolved DepositFile, not a Uri, so that there is no way to reach S3 from this
    // controller except through ResolveDepositFile. Same for ProxyFileWithByteRangeSupport.
    private async Task<IActionResult> ImageFromImageService(
        WorkspaceManager workspaceManager, DepositFile file, string size)
    {
        var parts = size.Split(',');
        var w = parts[0].Length > 0 && int.TryParse(parts[0], out var pw) ? pw : 0;
        var h = parts.Length > 1 && parts[1].Length > 0 && int.TryParse(parts[1], out var ph) ? ph : 0;
        // "," (or anything without a positive dimension) is not a size.
        if (w <= 0 && h <= 0)
            return new NotFoundResult();

        var fileUri = file.FileUri;
        var streamResult = await workspaceManager.GetStream(fileUri);
        if (streamResult is not { Success: true, Value.Item1: not null })
            return new NotFoundResult();

        // libvips reads its source synchronously, so read the S3 stream asynchronously first rather
        // than block a request thread on the network.
        using var buffered = new MemoryStream();
        await using (var source = streamResult.Value.Item1)
        {
            await source.CopyToAsync(buffered);
        }
        buffered.Position = 0;

        byte[] jpeg;
        try
        {
            jpeg = MediaImages.RenderJpeg(buffered, w, h);
        }
        catch (InvalidDataException e)
        {
            // A corrupt file, or one in a format the service doesn't preview: the viewer still gets an
            // image. Not cached, so a replacement file shows up as soon as it's there.
            logger.LogWarning(e, "Image service could not decode {FileUri}; serving the placeholder", fileUri);
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Remove(HeaderNames.ETag);
            return Placeholder(thumbnail: Math.Max(w, h) <= ThumbnailSize);
        }
        return new FileStreamResult(new MemoryStream(jpeg), "image/jpeg");
    }

    private static IActionResult InfoJson(string imageBaseUrl, WorkingFile? mediaItem)
    {
        var extents = mediaItem?.Metadata.OfType<ExtentMetadata>().SingleOrDefault();
        if (extents is not { PixelWidth: > 0, PixelHeight: > 0 })
            return new NotFoundResult();

        var original  = new IIIFSize(extents.PixelWidth.Value, extents.PixelHeight.Value);
        var mainSize  = IIIFSize.Confine(1200, original);
        var thumbSize = IIIFSize.Confine(100, original);

        var info = new Dictionary<string, object>
        {
            ["@context"] = "http://iiif.io/api/image/3/context.json",
            ["id"]       = imageBaseUrl,
            ["type"]     = "ImageService3",
            ["protocol"] = "http://iiif.io/api/image",
            ["profile"]  = "level0",
            ["width"]    = extents.PixelWidth.Value,
            ["height"]   = extents.PixelHeight.Value,
            ["sizes"]    = new[]
            {
                new { width = thumbSize.Width, height = thumbSize.Height },
                new { width = mainSize.Width,  height = mainSize.Height  }
            }
        };
        return new ContentResult
        {
            Content     = JsonSerializer.Serialize(info),
            ContentType = "application/json"
        };
    }
}
