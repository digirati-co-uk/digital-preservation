using Preservation.API.Features.ImportJobs.Requests;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// GetDiffImportJobHandler.IsRename decides whether the diff emits a rename for a resource the
/// Archival Group already has (issue #347). A METS file div with LABEL="" gives the source an
/// empty name; that must not become a rename, which the Storage API would refuse for the whole job.
/// </summary>
public class DiffRenameDetectionTests
{
    [Theory]
    [InlineData("Page one", "Page one (rescanned)")]
    [InlineData(null, "Page one")]
    [InlineData("page-001.tif", "Page one")]
    public void A_Different_Source_Name_Is_A_Rename(string? existingName, string sourceName)
    {
        GetDiffImportJobHandler.IsRename(existingName, sourceName).Should().BeTrue();
    }

    [Fact]
    public void The_Same_Name_Is_Not_A_Rename()
    {
        GetDiffImportJobHandler.IsRename("Page one", "Page one").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Source_With_No_Name_Is_Never_A_Rename(string? sourceName)
    {
        GetDiffImportJobHandler.IsRename("Page one", sourceName).Should().BeFalse();
    }
}
