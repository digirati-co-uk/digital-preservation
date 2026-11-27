using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Iiif;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// The placeholder endpoint is anonymous, so it renders only what the projection asks for: a
/// size the projection uses, a PRONOM-shaped key and a registered-name-shaped media type.
/// </summary>
public class PlaceholdersControllerTests
{
    private static PlaceholdersController Controller() => new()
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Theory]
    [InlineData("1000x800", "fmt", 353, "image", "tiff")]
    [InlineData("750x1000", "x-fmt", 111, "text", "plain")]
    [InlineData("1000x250", "fmt", 0, "application", "vnd.ms-excel")]
    public void A_Placeholder_The_Projection_Uses_Is_A_Cacheable_Png(string size, string prefix, int number, string main, string sub)
    {
        var controller = Controller();

        var result = controller.Canvas(size, prefix, number, main, sub);

        result.Should().BeOfType<FileContentResult>().Which.ContentType.Should().Be("image/png");
        controller.Response.Headers.CacheControl.ToString().Should().StartWith("public");
    }

    [Theory]
    [InlineData("1001x800", "fmt", 353, "image", "tiff")]     // not a size the projection uses
    [InlineData("5000x4000", "fmt", 353, "image", "tiff")]
    [InlineData("1000", "fmt", 353, "image", "tiff")]
    [InlineData("1000x800", "pronom", 353, "image", "tiff")]  // not a PRONOM prefix
    [InlineData("1000x800", "fmt", -1, "image", "tiff")]
    [InlineData("1000x800", "fmt", 353, "image", "<b>tiff</b>")]
    [InlineData("1000x800", "fmt", 353, "Image Files", "tiff")]
    public void Anything_Else_Is_Not_Found(string size, string prefix, int number, string main, string sub)
    {
        Controller().Canvas(size, prefix, number, main, sub).Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("image")]
    [InlineData("file")]
    public void A_Thumbnail_Is_A_Png(string kind)
    {
        Controller().Thumbnail(kind).Should().BeOfType<FileContentResult>();
    }

    [Fact]
    public void An_Unknown_Thumbnail_Is_Not_Found()
    {
        Controller().Thumbnail("spreadsheet").Should().BeOfType<NotFoundResult>();
    }
}
