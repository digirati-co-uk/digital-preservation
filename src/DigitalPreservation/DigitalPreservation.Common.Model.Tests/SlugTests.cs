using FluentAssertions;
namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// A slug becomes a path segment under a parent URI. Every character of "." and ".." is legal, but
/// as a whole each is a dot segment that resolves to the parent rather than naming a child - so an
/// upload called ".." produced a Binary whose id was the Archival Group itself.
/// </summary>
public class SlugTests
{
    [Theory]
    [InlineData("page-001.tif")]
    [InlineData(".hidden")]
    [InlineData("...")]
    [InlineData("thing.")]
    [InlineData("100%")]
    public void Ordinary_Names_Are_Valid_Slugs(string slug)
    {
        PreservedResource.ValidSlug(slug, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    [InlineData("%2E")]
    public void A_Dot_Segment_Is_Not_A_Valid_Slug(string slug)
    {
        PreservedResource.ValidSlug(slug, out var reason).Should().BeFalse();
        reason.Should().Contain("not allowed as slugs");
    }

    [Theory]
    [InlineData("a%2fb")]
    [InlineData("a%2Fb")]
    [InlineData("a%5cb")]
    [InlineData("..%2fx")]
    public void An_Encoded_Separator_Is_Not_A_Valid_Slug(string slug)
    {
        PreservedResource.ValidSlug(slug, out var reason).Should().BeFalse();
        reason.Should().Contain("encoded path separator");
    }

    [Theory]
    [InlineData(".", "-")]
    [InlineData("..", "--")]
    [InlineData("%2e%2e", "-2e-2e")]
    [InlineData("a%2Fb", "a-2fb")]
    [InlineData("...", "...")]
    [InlineData("My File.TIF", "my-file.tif")]
    public void MakeValidSlug_Never_Produces_A_Dot_Segment(string name, string expected)
    {
        PreservedResource.MakeValidSlug(name).Should().Be(expected);
    }
}
