using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.IIIF;

namespace Preservation.API.Features.Iiif;

/// <summary>
/// The placeholder images painted on the canvases of the vanilla IIIF projection (issue #261).
/// Anonymous, because the viewer showing a manifest can't send a token, and safe to be: an image
/// is chosen by a file's format, never by the file, so it says nothing about what is preserved.
/// </summary>
[Route("placeholders")]
[ApiController]
[AllowAnonymous]
[EnableCors("AllowAll")]
public class PlaceholdersController : ControllerBase
{
    private const string CacheControl = "public, max-age=604800";

    /// <summary>
    /// A canvas placeholder, e.g. <c>/placeholders/canvas/1000x800/fmt/353/image/tiff</c>. The size
    /// must be one the projection uses; <c>fmt/0</c> means the file has no PRONOM key.
    /// </summary>
    [HttpGet("canvas/{size}/{prefix}/{number:int}/{main}/{sub}", Name = "GetCanvasPlaceholder")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult Canvas(string size, string prefix, int number, string main, string sub)
    {
        if (!TryParseSize(size, out var placeholderSize)
            || !Placeholders.IsValidSize(placeholderSize)
            || !Placeholders.IsValidPronom(prefix, number)
            || !Placeholders.IsValidMediaTypePart(main)
            || !Placeholders.IsValidMediaTypePart(sub))
        {
            return NotFound();
        }
        Response.Headers.CacheControl = CacheControl;
        return File(Placeholders.RenderCanvas(placeholderSize, prefix, number, main, sub), "image/png");
    }

    /// <summary>A thumbnail placeholder: <c>image</c>, <c>video</c>, <c>audio</c> or <c>file</c>.</summary>
    [HttpGet("thumb/{kind}", Name = "GetThumbnailPlaceholder")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult Thumbnail(string kind)
    {
        if (kind is not ("image" or "video" or "audio" or "file"))
        {
            return NotFound();
        }
        Response.Headers.CacheControl = CacheControl;
        return File(Placeholders.RenderThumbnail(kind), "image/png");
    }

    private static bool TryParseSize(string value, out PlaceholderSize size)
    {
        size = default;
        var parts = value.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        {
            size = new PlaceholderSize(w, h);
            return true;
        }
        return false;
    }
}
