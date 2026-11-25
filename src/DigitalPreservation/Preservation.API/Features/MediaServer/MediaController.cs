using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.IIIF;

namespace Preservation.API.Features.MediaServer;

/// <summary>
/// What a viewer needs to show a Deposit's IIIF Manifest, under the Deposit's IIIF token: the
/// placeholder images painted on every canvas, and each original file, which the canvas links to
/// as its rendering. The Manifest paints nothing else (issue #261), so there is no image service.
/// </summary>
[Route("[controller]")]
[ApiController]
[EnableCors("AllowAll")]
public class MediaController(
    IMediator mediator,
    WorkspaceManagerFactory workspaceManagerFactory,
    ITokenService tokenService
) : ControllerBase
{
    private static readonly Lazy<byte[]> CanvasPlaceholder = new(() => MediaImages.GreyPng(1000, 800));
    private static readonly Lazy<byte[]> ThumbPlaceholder = new(() => MediaImages.GreyPng(100, 80));

    [AllowAnonymous]
    [RequireFeatureFlag("EnableIiifMediaEndpoints")]
    [HttpGet("{token}/{source}/{sourceId}/{type}/{**localPath}", Name = "GetMedia")]
    public async Task<IActionResult> GetMedia(
        [FromRoute] string token,
        [FromRoute] string source,
        [FromRoute] string sourceId,
        [FromRoute] string type,
        [FromRoute] string localPath)
    {
        if (source != "deposit")
            return this.ProblemFor(ErrorCodes.BadRequest, "Only 'deposit' is supported.", "Unsupported source");

        // The Manifest links to nothing else: imagesvc, video and audio went with the image
        // service (issue #261).
        if (type is not ("file" or "placeholder"))
            return NotFound();

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

        var file = ResolveDepositFile(workingDirectory, isBagIt, origin, localPath);
        if (file == null)
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.ETag = FileETag(localPath, file.Value.Item.Size);
        return await ProxyFileWithByteRangeSupport(workspaceManager, file.Value, HttpContext);
    }

    // A file's bytes are reached one way only: the path must name a file in this deposit's tree,
    // exactly, and only then is the S3 URI built. Every branch that touches S3 goes through here -
    // the image service's branch (since removed) once did not, and that was the traversal.
    // ValidateLocalPath is the defence in depth in front of this gate, not the gate.
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
}
