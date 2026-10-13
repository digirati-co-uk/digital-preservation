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

    private static WorkspaceManager CreateManager() =>
        new(
            new Deposit { Files = new Uri("https://preservation.test/deposits/d1/files"), ArchivalGroup = ArchivalGroupId },
            A.Fake<IMediator>(),
            A.Fake<IMetsParser>());

    [Fact]
    public void A_Folder_Whose_Name_Contains_A_Hash_Matches_The_Existing_Archival_Groups_Container()
    {
        const string folderName = "some #folder";
        var existingContainer = new Container
        {
            // Built the same way CombinedDirectory.ToContainer builds a destination Id for Fedora -
            // AppendEscapedSlug(slug.EscapeForUriNoHashes()) - so the '#' arrives as the repository's
            // stand-in, not a literal '#' or a plain percent-escape.
            Id = ArchivalGroupId.AppendEscapedSlug($"objects/{folderName}".EscapeForUriNoHashes()),
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
}
