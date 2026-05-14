using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Cors;
using IIIFSize = IIIF.Size;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.IIIF;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Preservation.API.Features.MediaServer;

[Route("[controller]")]
[ApiController]
[EnableCors("AllowAll")]
public class MediaController(
    ILogger<MediaController> logger,
    IMediator mediator,
    WorkspaceManagerFactory workspaceManagerFactory,
    ManifestBuilder manifestBuilder
) : ControllerBase
{
    private static readonly Lazy<byte[]> CanvasPlaceholder = new(() => GeneratePlaceholderPng(1000, 800));
    private static readonly Lazy<byte[]> ThumbPlaceholder = new(() => GeneratePlaceholderPng(100, 80));

    [AllowAnonymous]
    [HttpGet("{token}/{source}/{sourceId}/{type}/{**localPath}", Name = "GetMedia")]
    public async Task<IActionResult> GetMedia(
        [FromRoute] string token,
        [FromRoute] string source,
        [FromRoute] string sourceId,
        [FromRoute] string type,
        [FromRoute] string localPath)
    {
        if (source != "deposit")
        {
            throw new NotSupportedException("Only serve media from deposits for now");
        }

        if (!ValidateLocalPath(localPath))
        {
            return Unauthorized();
        }

        var key = manifestBuilder.GetKey(token);
        if (!key.HasText() || key.GetSlug() != sourceId)
        {
            return Unauthorized();
        }

        var depositResult = await mediator.Send(new GetDeposit(sourceId));
        if (depositResult is not { Success: true, Value: not null })
        {
            return NotFound();
        }

        var deposit = depositResult.Value;
        var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit);
        var workingDirectoryResult = await workspaceManager.GetFileSystemWorkingDirectory(false);
        var workingDirectory = workingDirectoryResult.Value;

        if (workingDirectory == null)
        {
            return NotFound();
        }

        if (type == "placeholder")
        {
            return ServePlaceholder(localPath);
        }

        // Detect BagIt layout from the working directory tree rather than relying on
        // workspaceManager.IsBagItLayout, which is only set after GetCombinedDirectory runs.
        var isBagIt = workingDirectory.Directories.Any(d => d.LocalPath == FolderNames.BagItData);
        var origin = FolderNames.GetFilesLocation(deposit.Files!, isBagIt);

        if (type == "imagesvc")
        {
            string realLocalPath;
            var elements = localPath.Split('/');

            if (elements[^1] == "info.json")
            {
                realLocalPath = localPath[..^"/info.json".Length];
                var mediaItem = workingDirectory.FindFile(FolderNames.GetPathPrefix(isBagIt) + realLocalPath);
                var imageBaseUrl = Request.GetDisplayUrl();
                imageBaseUrl = imageBaseUrl[..imageBaseUrl.LastIndexOf("/info.json", StringComparison.Ordinal)];
                return InfoJson(imageBaseUrl, mediaItem);
            }

            if (elements[^1] == "default.jpg" && elements[^2] == "0" && elements[^4] == "full")
            {
                var size = elements[^3];
                var imageApi = $"/full/{size}/0/default.jpg";
                realLocalPath = localPath[..^imageApi.Length];
                return await ImageFromImageService(workspaceManager, origin, realLocalPath, size);
            }

            // Bare imagesvc URL with no IIIF params — redirect to info.json if the file exists
            if (elements.Length >= 4)
            {
                var testRealLocalPath = string.Join('/', elements[..^4]);
                var testMediaItem = workingDirectory.FindFile(FolderNames.GetPathPrefix(isBagIt) + testRealLocalPath);
                if (testMediaItem != null)
                {
                    return Redirect(Request.GetDisplayUrl() + "/info.json");
                }
            }

            return NotFound();
        }

        var item = workingDirectory.FindFile(FolderNames.GetPathPrefix(isBagIt) + localPath);
        if (item == null)
        {
            return NotFound();
        }

        return await ProxyFileWithByteRangeSupport(workspaceManager, origin, localPath, item, HttpContext);
    }

    private static bool ValidateLocalPath(string localPath)
    {
        if (string.IsNullOrEmpty(localPath)) return false;
        foreach (var segment in localPath.Split('/'))
        {
            if (string.IsNullOrEmpty(segment) || segment == ".." || segment == "."
                || segment.Contains('\\') || segment.Contains('\0'))
                return false;
        }
        return true;
    }

    private static async Task<IActionResult> ProxyFileWithByteRangeSupport(
        WorkspaceManager workspaceManager, Uri origin, string localPath, WorkingFile item, HttpContext httpContext)
    {
        var fileUri = new Uri(origin.ToString().TrimEnd('/') + "/" + localPath);
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
                {
                    return new NotFoundResult();
                }

                // Compute the actual end byte for the Content-Range header.
                // item.Size should be populated from the deposit file system.
                var totalSize = item.Size;
                var actualTo = to ?? (totalSize.HasValue ? totalSize.Value - 1 : from);
                var totalPart = totalSize.HasValue ? totalSize.Value.ToString() : "*";

                httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
                httpContext.Response.Headers.ContentRange = $"bytes {from}-{actualTo}/{totalPart}";
                httpContext.Response.ContentLength = actualTo - from + 1;
                return new FileStreamResult(streamResult.Value, contentType);
            }
        }

        // No Range header — serve the full file
        var fullStreamResult = await workspaceManager.GetStream(fileUri);
        if (fullStreamResult is not { Success: true, Value: not null })
        {
            return new NotFoundResult();
        }
        if (item.Size.HasValue)
        {
            httpContext.Response.ContentLength = item.Size.Value;
        }
        return new FileStreamResult(fullStreamResult.Value, contentType);
    }

    private static IActionResult ServePlaceholder(string localPath)
    {
        var bytes = localPath == "thumb.png" ? ThumbPlaceholder.Value : CanvasPlaceholder.Value;
        return new FileContentResult(bytes, "image/png");
    }

    private static async Task<IActionResult> ImageFromImageService(
        WorkspaceManager workspaceManager, Uri origin, string realLocalPath, string size)
    {
        var fileUri = new Uri(origin.ToString().TrimEnd('/') + "/" + realLocalPath);
        var streamResult = await workspaceManager.GetStream(fileUri);
        if (streamResult is not { Success: true, Value: not null })
        {
            return new NotFoundResult();
        }

        var parts = size.Split(',');
        var w = parts[0].Length > 0 && int.TryParse(parts[0], out var pw) ? pw : 0;
        var h = parts.Length > 1 && parts[1].Length > 0 && int.TryParse(parts[1], out var ph) ? ph : 0;

        using var image = await Image.LoadAsync(streamResult.Value);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(w, h),
            Mode = ResizeMode.Max
        }));

        var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        ms.Position = 0;
        return new FileStreamResult(ms, "image/jpeg");
    }

    private static IActionResult InfoJson(string imageBaseUrl, WorkingFile? mediaItem)
    {
        var extents = mediaItem?.Metadata.OfType<ExtentMetadata>().SingleOrDefault();
        if (extents is not { PixelWidth: > 0, PixelHeight: > 0 })
        {
            return new NotFoundResult();
        }

        var original = new IIIFSize(extents.PixelWidth.Value, extents.PixelHeight.Value);
        var mainSize = IIIFSize.Confine(1200, original);
        var thumbSize = IIIFSize.Confine(100, original);

        var info = new Dictionary<string, object>
        {
            ["@context"] = "http://iiif.io/api/image/3/context.json",
            ["id"] = imageBaseUrl,
            ["type"] = "ImageService3",
            ["protocol"] = "http://iiif.io/api/image",
            ["profile"] = "level0",
            ["width"] = extents.PixelWidth.Value,
            ["height"] = extents.PixelHeight.Value,
            ["sizes"] = new[]
            {
                new { width = thumbSize.Width, height = thumbSize.Height },
                new { width = mainSize.Width, height = mainSize.Height }
            }
        };
        return new ContentResult
        {
            Content = JsonSerializer.Serialize(info),
            ContentType = "application/json"
        };
    }

    private static byte[] GeneratePlaceholderPng(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(0x88, 0x88, 0x88));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

}
