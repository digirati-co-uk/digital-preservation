using NetVips;

namespace Preservation.API.Features.MediaServer;

/// <summary>
/// The media server's only image code: the placeholder painted on every canvas of a Deposit's IIIF
/// Manifest. It decodes nothing a depositor supplied. libvips via NetVips.
/// </summary>
internal static class MediaImages
{
    public static byte[] GreyPng(int width, int height)
    {
        using var canvas = Image.Black(width, height);
        using var grey = canvas.NewFromImage(0x88, 0x88, 0x88);
        return grey.PngsaveBuffer();
    }
}
