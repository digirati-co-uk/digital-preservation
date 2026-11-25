using NetVips;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

public class PlaceholdersTests
{
    [Theory]
    [InlineData(null, null, 1000, 800)]      // no extents
    [InlineData(2000, null, 1000, 800)]
    [InlineData(4000, 3000, 1000, 750)]      // landscape keeps its ratio
    [InlineData(3000, 4000, 750, 1000)]      // portrait
    [InlineData(1000, 1000, 1000, 1000)]
    [InlineData(1920, 1080, 1000, 550)]      // 562.5 rounds to a multiple of 50
    [InlineData(10000, 100, 1000, 250)]      // a panorama is clamped to 4:1
    [InlineData(100, 10000, 250, 1000)]
    [InlineData(300, 200, 1000, 650)]        // small images are scaled up to the 1000 long side
    public void The_Size_Follows_The_Canvas_Ratio_Within_Limits(int? width, int? height, int expectedWidth, int expectedHeight)
    {
        var size = Placeholders.SizeFor(width, height);

        size.Should().Be(new PlaceholderSize(expectedWidth, expectedHeight));
        Placeholders.IsValidSize(size).Should().BeTrue();
    }

    [Theory]
    [InlineData(999, 800)]
    [InlineData(1000, 801)]
    [InlineData(1050, 800)]
    [InlineData(200, 1000)]
    [InlineData(800, 800)]                   // neither side is the long side
    [InlineData(0, 0)]
    public void Only_Sizes_The_Projection_Uses_Are_Valid(int width, int height)
    {
        Placeholders.IsValidSize(new PlaceholderSize(width, height)).Should().BeFalse();
    }

    [Theory]
    [InlineData("fmt/353", "image/tiff", "canvas/1000x800/fmt/353/image/tiff")]
    [InlineData("x-fmt/111", "text/plain; charset=utf-8", "canvas/1000x800/x-fmt/111/text/plain")]
    [InlineData(null, "Application/PDF", "canvas/1000x800/fmt/0/application/pdf")]
    [InlineData("not a key", null, "canvas/1000x800/fmt/0/application/octet-stream")]
    [InlineData("fmt/12", "image/svg+xml", "canvas/1000x800/fmt/12/image/svg+xml")]
    public void The_Canvas_Path_Is_Keyed_By_Format_Only(string? pronom, string? mediaType, string expected)
    {
        Placeholders.CanvasPath(Placeholders.DefaultSize, pronom, mediaType).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/jpeg", "thumb/image")]
    [InlineData("video/mp4", "thumb/video")]
    [InlineData("audio/x-wav", "thumb/audio")]
    [InlineData("application/pdf", "thumb/file")]
    [InlineData(null, "thumb/file")]
    public void There_Are_Four_Thumbnails(string? mediaType, string expected)
    {
        Placeholders.ThumbnailPath(mediaType).Should().Be(expected);
    }

    [Theory]
    [InlineData(1000, 800)]
    [InlineData(750, 1000)]
    [InlineData(1000, 250)]
    public void A_Canvas_Placeholder_Is_A_Png_Of_The_Requested_Size(int width, int height)
    {
        var png = Placeholders.RenderCanvas(new PlaceholderSize(width, height), "fmt", 353, "image", "tiff");

        using var image = Image.NewFromBuffer(png);
        image.Get("vips-loader").Should().Be("pngload_buffer");
        image.Width.Should().Be(width);
        image.Height.Should().Be(height);
        // The text was drawn: the image is not a flat background.
        image.Deviate().Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_Thumbnail_Is_A_Png_Of_The_Thumbnail_Size()
    {
        using var image = Image.NewFromBuffer(Placeholders.RenderThumbnail("video"));

        image.Width.Should().Be(Placeholders.ThumbnailSize.Width);
        image.Height.Should().Be(Placeholders.ThumbnailSize.Height);
        image.Deviate().Should().BeGreaterThan(0);
    }
}
