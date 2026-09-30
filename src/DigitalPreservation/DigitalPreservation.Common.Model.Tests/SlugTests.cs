using DigitalPreservation.Utils;
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
    public void Ordinary_Names_Are_Valid_Slugs(string slug)
    {
        PreservedResource.ValidSlug(slug, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData("Teto in tree.png")]
    [InlineData("été.tif")]
    [InlineData("a&b (1).txt")]
    [InlineData("plus+sign.txt")]
    [InlineData("REPORT~1.DOC")]
    [InlineData("~$budget.xlsx")]
    [InlineData("notes#2.txt")]
    public void A_Deposit_File_Name_Escapes_To_A_Valid_Slug_And_Back(string originalName)
    {
        // This is the main point of issue #298: every name a deposit file can actually have
        // (born-digital accessions included - DOS 8.3 short names, Office lock files) must
        // round-trip through the same escaping CombinedDirectory.ToContainer uses.
        var slug = originalName.EscapeForUriNoHashes();

        PreservedResource.ValidSlug(slug, out var reason).Should().BeTrue(reason);
        slug.UnEscapeFromUriNoHashes().Should().Be(originalName);
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("a%zz")]
    [InlineData("%")]
    public void A_Percent_Not_Starting_A_Well_Formed_Escape_Is_Not_A_Valid_Slug(string slug)
    {
        // No escaped name produces these - Uri.EscapeDataString turns a literal '%' into '%25' - so
        // they can only come from a caller building ids directly, and don't decode to a sensible name.
        PreservedResource.ValidSlug(slug, out var reason).Should().BeFalse();
        reason.Should().Contain("must begin a percent-encoded escape");
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

    [Theory]
    [InlineData("a%b", "a-b")]
    [InlineData("a~b", "a~b")]
    public void MakeValidSlug_Maps_A_Stray_Percent_To_A_Hyphen_And_Keeps_Tilde(string name, string expected)
    {
        PreservedResource.MakeValidSlug(name).Should().Be(expected);
    }
}
