using DigitalPreservation.Common.Model.Import;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// AllResourceIds replaces the private seven-list collector the Preservation API's
/// ImportJobsController used to define for itself (issue #388 item 2.7), moved here next to
/// RenameRefusalMessage so both APIs - and this suite - can use the one definition of "every
/// resource id this job names".
/// </summary>
public class ImportJobAllResourceIdsTests
{
    [Fact]
    public void Returns_Empty_ForAJobWithNoResources()
    {
        new ImportJob().AllResourceIds().Should().BeEmpty();
    }

    [Fact]
    public void Collects_Ids_From_Every_One_Of_The_Seven_Lists()
    {
        var job = new ImportJob();
        job.BinariesToAdd.Add(new Binary { Id = new Uri("https://example.com/repo/thing/add.tif") });
        job.BinariesToPatch.Add(new Binary { Id = new Uri("https://example.com/repo/thing/patch.tif") });
        job.BinariesToDelete.Add(new Binary { Id = new Uri("https://example.com/repo/thing/delete.tif") });
        job.BinariesToRename.Add(new Binary { Id = new Uri("https://example.com/repo/thing/rename.tif") });
        job.ContainersToAdd.Add(new Container { Id = new Uri("https://example.com/repo/thing/add") });
        job.ContainersToDelete.Add(new Container { Id = new Uri("https://example.com/repo/thing/delete") });
        job.ContainersToRename.Add(new Container { Id = new Uri("https://example.com/repo/thing/rename") });

        var ids = job.AllResourceIds().ToList();

        ids.Should().HaveCount(7);
        ids.Should().Contain(new Uri("https://example.com/repo/thing/add.tif"));
        ids.Should().Contain(new Uri("https://example.com/repo/thing/patch.tif"));
        ids.Should().Contain(new Uri("https://example.com/repo/thing/delete.tif"));
        ids.Should().Contain(new Uri("https://example.com/repo/thing/rename.tif"));
        ids.Should().Contain(new Uri("https://example.com/repo/thing/add"));
        ids.Should().Contain(new Uri("https://example.com/repo/thing/delete"));
        ids.Should().Contain(new Uri("https://example.com/repo/thing/rename"));
    }

    [Fact]
    public void Includes_A_Null_Id_Rather_Than_Skipping_It()
    {
        // The caller's own job - refusing a resource with no id - relies on this: filtering nulls
        // out here would silently let an id-less binary or container through unreported.
        var job = new ImportJob();
        job.ContainersToDelete.Add(new Container());

        job.AllResourceIds().Should().ContainSingle(id => id == null);
    }
}
