using NetVips;
using Preservation.API.Features.MediaServer;

namespace Preservation.API.Tests.Features.MediaServer;

/// <summary>
/// The image service's pixels: what a deposit file is turned into for the level-0 /full/{w,h}/0/default.jpg
/// request, and the grey placeholders.
/// </summary>
public class MediaImagesTests
{
    public static IEnumerable<object[]> PreviewFormats()
    {
        yield return [".jpg"];
        yield return [".png"];
        yield return [".tif"];
        yield return [".webp"];
        yield return [".gif"];
    }

    [Theory]
    [MemberData(nameof(PreviewFormats))]
    public void Every_Preview_Format_Renders_As_A_Jpeg_At_The_Requested_Width(string format)
    {
        var original = Encode(Rgb(400, 300, 200, 100, 50), format);

        using var rendered = Render(original, 100, 0);

        rendered.Width.Should().Be(100);
        rendered.Height.Should().Be(75);
        rendered.Bands.Should().Be(3);
    }

    [Theory]
    [InlineData(100, 100, 100, 75)]   // fits within the box, keeping the aspect ratio
    [InlineData(0, 75, 100, 75)]      // height only
    [InlineData(1000, 0, 400, 300)]   // never upscales
    [InlineData(1000, 1000, 400, 300)]
    public void Sizes_Are_Confined_Never_Stretched(int width, int height, int expectedWidth, int expectedHeight)
    {
        var original = Encode(Rgb(400, 300, 200, 100, 50), ".tif");

        using var rendered = Render(original, width, height);

        rendered.Width.Should().Be(expectedWidth);
        rendered.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void A_16_Bit_Tiff_Is_Scaled_To_8_Bit_Not_Clipped()
    {
        // Mid-grey in 16 bits. Cast straight to 8 bits it would clip to white.
        using var canvas = Image.Black(40, 30);
        using var ushortCanvas = canvas.Cast(Enums.BandFormat.Ushort);
        using var grey16 = ushortCanvas.NewFromImage(30000, 30000, 30000);
        using var rgb16 = grey16.Copy(interpretation: Enums.Interpretation.Rgb16);
        rgb16.Format.Should().Be(Enums.BandFormat.Ushort);

        using var rendered = Render(rgb16.TiffsaveBuffer(), 40, 0);

        rendered.Avg().Should().BeApproximately(30000 / 257.0, 3);
    }

    [Fact]
    public void A_Cmyk_Tiff_Becomes_An_Rgb_Jpeg()
    {
        // Browsers render CMYK JPEGs badly or not at all.
        using var canvas = Image.Black(40, 30);
        using var cyan = canvas.NewFromImage(255, 0, 0, 0);
        using var cmyk = cyan.Copy(interpretation: Enums.Interpretation.Cmyk);

        using var rendered = Render(cmyk.TiffsaveBuffer(), 40, 0);

        rendered.Bands.Should().Be(3);
        rendered.Interpretation.Should().Be(Enums.Interpretation.Srgb);
        using var red = rendered.ExtractBand(0);
        using var blue = rendered.ExtractBand(2);
        red.Avg().Should().BeLessThan(blue.Avg(), "cyan has far more blue than red");
    }

    [Fact]
    public void Transparency_Is_Flattened_Onto_White()
    {
        using var canvas = Image.Black(40, 30);
        using var transparent = canvas.NewFromImage(0, 0, 0, 0);
        using var rgba = transparent.Copy(interpretation: Enums.Interpretation.Srgb);

        using var rendered = Render(rgba.PngsaveBuffer(), 40, 0);

        rendered.Bands.Should().Be(3);
        rendered.Avg().Should().BeApproximately(255, 1);
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("avif")]
    [InlineData("matrix")]
    public void A_Format_Outside_The_Preview_Set_Is_Refused(string format)
    {
        // NetVips.Native can load all three of these from a stream (checked with the block removed),
        // so this fails because the loader is blocked, not because it is missing.
        var original = format switch
        {
            "svg" => """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="30"><rect width="40" height="30" fill="red"/></svg>"""u8.ToArray(),
            "matrix" => "2 2\n1 2\n3 4\n"u8.ToArray(),
            _ => Encode(Rgb(40, 30, 1, 2, 3), ".avif")
        };

        var render = () => MediaImages.RenderJpeg(new MemoryStream(original), 40, 0);

        render.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_Corrupt_File_Is_Refused_As_Invalid_Data_Not_A_Library_Exception()
    {
        // Starts like a JPEG, then isn't one.
        byte[] corrupt = [0xFF, 0xD8, 0xFF, 0xE0, .. "not really a jpeg"u8.ToArray()];

        var render = () => MediaImages.RenderJpeg(new MemoryStream(corrupt), 40, 0);

        render.Should().Throw<InvalidDataException>().WithInnerException<VipsException>();
    }

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

    private static Image Render(byte[] original, int width, int height)
    {
        var jpeg = MediaImages.RenderJpeg(new MemoryStream(original), width, height);
        var rendered = Image.NewFromBuffer(jpeg);
        rendered.Get("vips-loader").Should().Be("jpegload_buffer");
        return rendered;
    }

    private static Image Rgb(int width, int height, double r, double g, double b)
    {
        using var canvas = Image.Black(width, height);
        using var colour = canvas.NewFromImage(r, g, b);
        return colour.Copy(interpretation: Enums.Interpretation.Srgb);
    }

    private static byte[] Encode(Image image, string format)
    {
        using (image)
        {
            return image.WriteToBuffer(format);
        }
    }
}
