using NetVips;
using Preservation.API.Features.MediaServer;

namespace Preservation.API.Tests.Features.MediaServer;

/// <summary>
/// The grey placeholders painted on every canvas of a Deposit's IIIF Manifest.
/// </summary>
public class MediaImagesTests
{
    [Theory]
    [InlineData(1000, 800)]
    [InlineData(100, 80)]
    public void The_Placeholder_Is_A_Grey_Png_Of_The_Given_Size(int width, int height)
    {
        using var png = Image.NewFromBuffer(MediaImages.GreyPng(width, height));

        png.Get("vips-loader").Should().Be("pngload_buffer");
        png.Width.Should().Be(width);
        png.Height.Should().Be(height);
        png.Bands.Should().Be(3);
        png.Avg().Should().Be(0x88);
    }
}
