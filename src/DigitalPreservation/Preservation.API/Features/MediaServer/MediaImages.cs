using NetVips;

namespace Preservation.API.Features.MediaServer;

/// <summary>
/// The only code in the media server that decodes or encodes pixels, kept in one place so the image
/// library behind it can be swapped without touching the controller. libvips via NetVips, which
/// replaced ImageSharp when ImageSharp's security fixes became licence-key-only.
/// </summary>
internal static class MediaImages
{
    // libvips's largest image dimension, used as "no constraint" for the side a IIIF size leaves empty.
    private const int Unconstrained = 10_000_000;

    // The formats a deposit image can be previewed from. libvips can load many more (SVG, HEIF, JPEG 2000,
    // FITS, its own .v format...), and every loader is attack surface on files a depositor supplied.
    // Blocking a libvips class blocks its subclasses too, so these names cover the file, buffer and
    // stream variants of each loader.
    private static readonly string[] PreviewLoaders =
    [
        "VipsForeignLoadJpeg",
        "VipsForeignLoadPng",
        "VipsForeignLoadTiff",
        "VipsForeignLoadWebp",
        "VipsForeignLoadNsgif"
    ];

    static MediaImages()
    {
        // Each request decodes a different file once, so libvips's operation cache would only hold
        // decoded pixels in memory without ever being hit.
        Cache.Max = 0;
        Operation.Block("VipsForeignLoad", true);
        foreach (var loader in PreviewLoaders)
        {
            Operation.Block(loader, false);
        }
    }

    /// <summary>
    /// A JPEG of the source image that fits within width x height, keeping its aspect ratio. A width
    /// or height of 0 leaves that side unconstrained. Never upscales: a level-0 service only offers
    /// sizes up to the original's.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The source is corrupt, or not in one of the preview formats. Thrown in place of the image
    /// library's own exception, so callers don't depend on which library this is.
    /// </exception>
    public static byte[] RenderJpeg(Stream source, int width, int height)
    {
        try
        {
            // Shrinks while loading where the format allows it, so a large original is never fully decoded.
            using var thumbnail = Image.ThumbnailStream(source,
                width > 0 ? width : Unconstrained,
                height: height > 0 ? height : Unconstrained,
                size: Enums.Size.Down);
            // thumbnail colour-manages CMYK to sRGB and jpegsave scales 16-bit to 8; MediaImagesTests holds
            // libvips to both. Left to jpegsave, transparency would become black.
            if (!thumbnail.HasAlpha())
            {
                return thumbnail.JpegsaveBuffer(keep: Enums.ForeignKeep.None);
            }
            using var opaque = thumbnail.Flatten(background: [255, 255, 255]);
            return opaque.JpegsaveBuffer(keep: Enums.ForeignKeep.None);
        }
        catch (VipsException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }

    public static byte[] GreyPng(int width, int height)
    {
        using var canvas = Image.Black(width, height);
        using var grey = canvas.NewFromImage(0x88, 0x88, 0x88);
        return grey.PngsaveBuffer();
    }
}
