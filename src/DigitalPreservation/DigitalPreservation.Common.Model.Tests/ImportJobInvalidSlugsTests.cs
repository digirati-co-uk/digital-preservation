using DigitalPreservation.Common.Model.Import;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// ItemsWithInvalidSlugs only checks the Add lists, plus Rename (which #260 refuses outright
/// regardless of slug validity). Patch and Delete name resources that already exist in Fedora, and
/// must stay operable through the UI even if the slug alphabet tightens (issue #298).
/// </summary>
public class ImportJobInvalidSlugsTests
{
    // '@' is legal in a URI path segment (so Uri doesn't rewrite it, unlike a genuinely malformed
    // '%' escape - Uri normalises "a%zzb" to "a%25zzb" at construction) but is not a valid slug
    // character, so this reliably produces an id whose slug ValidSlug refuses.
    private static Binary InvalidSlugBinary() =>
        new() { Id = new Uri("https://example.com/repo/thing/report@final.txt") };

    private static Container InvalidSlugContainer() =>
        new() { Id = new Uri("https://example.com/repo/thing/folder@name") };

    [Fact]
    public void An_Invalid_Slug_In_BinariesToPatch_Is_Ignored()
    {
        var job = new ImportJob();
        job.BinariesToPatch.Add(InvalidSlugBinary());

        var (items, message) = job.ItemsWithInvalidSlugs();

        items.Should().BeEmpty();
        message.Should().BeNull();
    }

    [Fact]
    public void An_Invalid_Slug_In_BinariesToDelete_Is_Ignored()
    {
        var job = new ImportJob();
        job.BinariesToDelete.Add(InvalidSlugBinary());

        var (items, message) = job.ItemsWithInvalidSlugs();

        items.Should().BeEmpty();
        message.Should().BeNull();
    }

    [Fact]
    public void An_Invalid_Slug_In_ContainersToDelete_Is_Ignored()
    {
        var job = new ImportJob();
        job.ContainersToDelete.Add(InvalidSlugContainer());

        var (items, message) = job.ItemsWithInvalidSlugs();

        items.Should().BeEmpty();
        message.Should().BeNull();
    }

    [Fact]
    public void An_Invalid_Slug_In_BinariesToAdd_Is_Flagged()
    {
        var job = new ImportJob();
        job.BinariesToAdd.Add(InvalidSlugBinary());

        var (items, message) = job.ItemsWithInvalidSlugs();

        items.Should().ContainSingle();
        message.Should().NotBeNull();
    }

    [Fact]
    public void An_Invalid_Slug_In_ContainersToAdd_Is_Flagged()
    {
        var job = new ImportJob();
        job.ContainersToAdd.Add(InvalidSlugContainer());

        var (items, message) = job.ItemsWithInvalidSlugs();

        items.Should().ContainSingle();
        message.Should().NotBeNull();
    }

    [Fact]
    public void An_Invalid_Slug_In_ContainersToRename_Is_Still_Flagged()
    {
        var job = new ImportJob();
        job.ContainersToRename.Add(InvalidSlugContainer());

        var (items, message) = job.ItemsWithInvalidSlugs();

        items.Should().ContainSingle();
        message.Should().NotBeNull();
    }
}
