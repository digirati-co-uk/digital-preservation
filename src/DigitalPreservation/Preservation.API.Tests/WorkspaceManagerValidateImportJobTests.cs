using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using FakeItEasy;
using MediatR;

namespace Preservation.API.Tests;

/// <summary>
/// ValidateImportJob checks that every folder/file the incoming METS mentions is accounted for -
/// already in the existing Archival Group, or being added by this import job. The
/// container-existence check compared Container.Id.LocalPath - which carries the repository's
/// custom stand-in for '#' (UriX.HashReplacement), since Uri.LocalPath leaves it alone rather
/// than percent-decoding it - directly against the METS's own literal localPath, so a folder
/// whose name contains '#' never matched and the diff failed outright (issue #243). Every
/// neighbouring check in the same method (the file-existence check, and both "being added in this
/// operation" checks) already goes through EscapePathElementsNoHashes to put both sides into the
/// same form; this was the one comparison that forgot it.
/// </summary>
public class WorkspaceManagerValidateImportJobTests
{
    private static readonly Uri ArchivalGroupId = new("https://preservation.test/repository/cc/thing");

    private static WorkspaceManager CreateManagerFor(Uri archivalGroupId) =>
        new(
            new Deposit { Files = new Uri("https://preservation.test/deposits/d1/files"), ArchivalGroup = archivalGroupId },
            A.Fake<IMediator>(),
            A.Fake<IMetsParser>());

    private static WorkspaceManager CreateManager() => CreateManagerFor(ArchivalGroupId);

    /// <summary>
    /// Builds a Container Id the same way CombinedDirectory.ToContainer actually builds one for
    /// Fedora: repositoryUri.AppendEscapedSlug(slug.EscapeForUriNoHashes()), one path segment at a
    /// time, recursing down from the Archival Group's own Id. Appending all the segments as one
    /// pre-joined string (e.g. AppendEscapedSlug($"objects/{folderName}".EscapeForUriNoHashes()))
    /// is NOT equivalent - EscapeForUriNoHashes escapes '/' too, so a multi-segment path handed to
    /// it in one call comes out with its slashes themselves escaped to %2F, and the test then only
    /// passes because Uri.LocalPath happens to decode %2F back again (issue #243 review: this was
    /// masking the real escaping bug the regression test below exists to catch).
    /// </summary>
    private static Uri ChildId(Uri archivalGroupId, params string[] segments) =>
        segments.Aggregate(archivalGroupId, (uri, segment) => uri.AppendEscapedSlug(segment.EscapeForUriNoHashes()));

    [Fact]
    public void A_Folder_Whose_Name_Contains_A_Hash_Matches_The_Existing_Archival_Groups_Container()
    {
        const string folderName = "some #folder";
        var existingContainer = new Container
        {
            // '#' arrives as the repository's stand-in (UriX.HashReplacement), not a literal '#' or
            // a plain percent-escape - see ChildId's remarks on why this must be segment-by-segment.
            Id = ChildId(ArchivalGroupId, "objects", folderName),
            Name = folderName
        };
        var existingArchivalGroup = new ArchivalGroup
        {
            Id = ArchivalGroupId,
            Containers = [existingContainer]
        };

        // A METS-only deposit: the folder is described in the METS but was never written to the
        // deposit's own filesystem, so DirectoryInDeposit is null and DirectoryInMets carries the
        // literal, un-substituted name.
        var depositRoot = new CombinedDirectory(directoryInDeposit: null, directoryInMets: null)
        {
            Directories =
            [
                new CombinedDirectory(
                    directoryInDeposit: null,
                    directoryInMets: new WorkingDirectory { LocalPath = $"objects/{folderName}", Name = folderName })
            ]
        };

        var importJob = new ImportJob { ArchivalGroup = ArchivalGroupId };

        var result = CreateManager().ValidateImportJob(importJob, depositRoot, existingArchivalGroup);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public void A_Folder_Whose_Name_Contains_A_Hash_And_Is_Missing_Everywhere_Still_Fails()
    {
        // Guards against a fix that makes the comparison vacuously true instead of correctly
        // escaped: a '#' folder genuinely absent from both the Archival Group and this import
        // job's ContainersToAdd must still be reported, with the same message as any other
        // missing folder.
        const string folderName = "some #folder";
        var existingArchivalGroup = new ArchivalGroup { Id = ArchivalGroupId };

        var depositRoot = new CombinedDirectory(directoryInDeposit: null, directoryInMets: null)
        {
            Directories =
            [
                new CombinedDirectory(
                    directoryInDeposit: null,
                    directoryInMets: new WorkingDirectory { LocalPath = $"objects/{folderName}", Name = folderName })
            ]
        };

        var importJob = new ImportJob { ArchivalGroup = ArchivalGroupId };

        var result = CreateManager().ValidateImportJob(importJob, depositRoot, existingArchivalGroup);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain($"objects/{folderName}");
    }

    /// <summary>
    /// Issue #243 review: the original fix escaped only the localPath side of the comparison,
    /// leaving agLocalPathWithSlash (Uri.LocalPath, unescaped) bare on the right. ValidSlug allows
    /// '(', ')' and '%XX' in an Archival Group's own slug, so for an AG at .../cc/thing(1), the left
    /// side (c.Id.LocalPath, escaped) had "thing%281%29" while the right side had the literal
    /// "thing(1)" - never equal. That failed nearly every diff against such a group (any METS with
    /// an objects folder), on top of the pre-existing, same-shaped bug already on main for
    /// BinariesToAdd and ContainersToAdd.
    /// </summary>
    [Theory]
    [InlineData("thing(1)")]
    [InlineData("my%20thing")]
    public void A_Folder_Matches_Against_An_Archival_Group_Whose_Own_Path_Needs_Escaping(string agSlug)
    {
        var archivalGroupId = new Uri($"https://preservation.test/repository/cc/{agSlug}");
        const string folderName = "objects-subfolder";
        var existingContainer = new Container
        {
            Id = ChildId(archivalGroupId, "objects", folderName),
            Name = folderName
        };
        var existingArchivalGroup = new ArchivalGroup
        {
            Id = archivalGroupId,
            Containers = [existingContainer]
        };

        var depositRoot = new CombinedDirectory(directoryInDeposit: null, directoryInMets: null)
        {
            Directories =
            [
                new CombinedDirectory(
                    directoryInDeposit: null,
                    directoryInMets: new WorkingDirectory { LocalPath = $"objects/{folderName}", Name = folderName })
            ]
        };

        var importJob = new ImportJob { ArchivalGroup = archivalGroupId };

        var result = CreateManagerFor(archivalGroupId).ValidateImportJob(importJob, depositRoot, existingArchivalGroup);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }
}
