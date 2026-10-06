using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace.Requests;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Preservation.API.Tests.Workspace;

public class DeleteItemsHandlerTests
{
    private readonly IAmazonS3 s3Client = A.Fake<IAmazonS3>();
    private readonly IMetsManager metsManager = A.Fake<IMetsManager>();
    private readonly DeleteItemsHandler handler;
    private const string DepositETag = "current-etag";
    private static readonly Uri DepositFiles = new("s3://test-bucket/deposits/dep-1/deposit-files/");

    public DeleteItemsHandlerTests()
    {
        handler = new DeleteItemsHandler(s3Client, metsManager, NullLogger<DeleteItemsHandler>.Instance);
    }

    private static (CombinedDirectory root, FullMets mets) BuildFixture(params string[] fileNames)
    {
        var depositObjects = new WorkingDirectory { LocalPath = "objects", Name = "objects" };
        var metsObjects = new WorkingDirectory { LocalPath = "objects", Name = "objects" };
        foreach (var name in fileNames)
        {
            depositObjects.Files.Add(new WorkingFile { LocalPath = $"objects/{name}", Name = name });
            metsObjects.Files.Add(new WorkingFile { LocalPath = $"objects/{name}", Name = name });
        }

        var depositRoot = new WorkingDirectory { LocalPath = "", Name = WorkingDirectory.DefaultRootName };
        depositRoot.Directories.Add(depositObjects);
        var metsRoot = new WorkingDirectory { LocalPath = "", Name = WorkingDirectory.DefaultRootName };
        metsRoot.Directories.Add(metsObjects);

        var combinedRoot = CombinedBuilder.Build(depositRoot, metsRoot);

        var mets = new FullMets
        {
            Mets = new DigitalPreservation.XmlGen.Mets.Mets(),
            Uri = new Uri("https://example.org/deposits/dep-1/mets.xml")
        };
        foreach (var name in fileNames)
        {
            mets.PhysicalDivsByPath[$"objects/{name}"] = new DigitalPreservation.XmlGen.Mets.DivType();
        }

        return (combinedRoot, mets);
    }

    private void SetUpS3Delete(string fileName, bool succeeds)
    {
        A.CallTo(() => s3Client.DeleteObjectAsync(
                A<DeleteObjectRequest>.That.Matches(r => r.Key.EndsWith(fileName)),
                A<CancellationToken>._))
            .Returns(Task.FromResult(new DeleteObjectResponse
            {
                HttpStatusCode = succeeds ? HttpStatusCode.NoContent : HttpStatusCode.InternalServerError
            }));
    }

    private void SetUpMetsManager(FullMets mets)
    {
        A.CallTo(() => metsManager.GetFullMets(DepositFiles, DepositETag))
            .Returns(Task.FromResult(Result.OkNotNull(mets)));

        A.CallTo(() => metsManager.DeleteFromMets(A<FullMets>._, A<string>._))
            .Invokes((FullMets m, string path) => m.PhysicalDivsByPath.Remove(path))
            .Returns(Result.Ok());
    }

    private static DeleteSelection SelectionFor(params string[] fileNames) => new()
    {
        DeleteFromMets = true,
        DeleteFromDepositFiles = true,
        Items = fileNames.Select(name => new MinimalItem
        {
            RelativePath = $"objects/{name}",
            IsDirectory = false,
            Whereabouts = Whereabouts.Both
        }).ToList()
    };

    [Fact]
    public async Task Handle_WritesMetsForItemsDeletedBeforeFailure_AndReturnsFailure()
    {
        var (combinedRoot, mets) = BuildFixture("a.txt", "b.txt", "c.txt");
        SetUpS3Delete("a.txt", succeeds: true);
        SetUpS3Delete("b.txt", succeeds: true);
        SetUpS3Delete("c.txt", succeeds: false);
        SetUpMetsManager(mets);

        FullMets? metsPassedToWrite = null;
        A.CallTo(() => metsManager.WriteMets(A<FullMets>._))
            .Invokes((FullMets m) => metsPassedToWrite = m)
            .Returns(Result.Ok());

        var request = new DeleteItems(
            isBagItLayout: false,
            depositFiles: DepositFiles,
            deleteSelection: SelectionFor("a.txt", "b.txt", "c.txt"),
            combinedRootDirectory: combinedRoot,
            depositETag: DepositETag);

        var result = await handler.Handle(request, CancellationToken.None);

        result.Failure.Should().BeTrue();
        A.CallTo(() => metsManager.WriteMets(A<FullMets>._)).MustHaveHappenedOnceExactly();
        metsPassedToWrite.Should().NotBeNull();
        metsPassedToWrite!.PhysicalDivsByPath.Should().NotContainKey("objects/a.txt");
        metsPassedToWrite.PhysicalDivsByPath.Should().NotContainKey("objects/b.txt");
        metsPassedToWrite.PhysicalDivsByPath.Should().ContainKey("objects/c.txt");
    }

    [Fact]
    public async Task Handle_OmitsEmptyParenthetical_WhenNoItemSucceededBeforeFailure()
    {
        var (combinedRoot, mets) = BuildFixture("a.txt");
        SetUpS3Delete("a.txt", succeeds: false);
        SetUpMetsManager(mets);

        var request = new DeleteItems(
            isBagItLayout: false,
            depositFiles: DepositFiles,
            deleteSelection: SelectionFor("a.txt"),
            combinedRootDirectory: combinedRoot,
            depositETag: DepositETag);

        var result = await handler.Handle(request, CancellationToken.None);

        result.Failure.Should().BeTrue();
        // No item succeeded before the failure, so there is nothing to list - the message must not
        // carry an empty "()" - and the underlying error text already ends with a full stop, so the
        // message must not double it up.
        result.ErrorMessage.Should().NotContain("(").And.NotContain("..");
        result.ErrorMessage.Should().StartWith("Delete failed after 0 items.");
    }

    [Fact]
    public async Task Handle_ListsDeletedPaths_WhenSomeItemsSucceededBeforeFailure()
    {
        var (combinedRoot, mets) = BuildFixture("a.txt", "b.txt");
        SetUpS3Delete("a.txt", succeeds: true);
        SetUpS3Delete("b.txt", succeeds: false);
        SetUpMetsManager(mets);
        A.CallTo(() => metsManager.WriteMets(A<FullMets>._)).Returns(Result.Ok());

        var request = new DeleteItems(
            isBagItLayout: false,
            depositFiles: DepositFiles,
            deleteSelection: SelectionFor("a.txt", "b.txt"),
            combinedRootDirectory: combinedRoot,
            depositETag: DepositETag);

        var result = await handler.Handle(request, CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().StartWith("Delete failed after 1 items (objects/a.txt).");
        result.ErrorMessage.Should().NotContain("..");
    }

    /// <summary>
    /// Issue #259: a failure on a path ContinueIfFail lists must be carried past, not reported as a
    /// successful deletion, and must not stop the rest of the request.
    /// </summary>
    [Fact]
    public async Task Handle_Tolerates_A_Listed_Failure_Without_Reporting_It_As_Deleted_And_Continues()
    {
        var (combinedRoot, mets) = BuildFixture("a.txt", "b.txt", "c.txt");
        SetUpS3Delete("a.txt", succeeds: true);
        SetUpS3Delete("b.txt", succeeds: false);
        SetUpS3Delete("c.txt", succeeds: true);
        SetUpMetsManager(mets);
        A.CallTo(() => metsManager.WriteMets(A<FullMets>._)).Returns(Result.Ok());

        var selection = SelectionFor("a.txt", "b.txt", "c.txt");
        selection.ContinueIfFail = ["objects/b.txt"];

        var request = new DeleteItems(
            isBagItLayout: false,
            depositFiles: DepositFiles,
            deleteSelection: selection,
            combinedRootDirectory: combinedRoot,
            depositETag: DepositETag);

        var result = await handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue("a tolerated failure must not abort the rest of the deletion");
        result.Value!.Items.Select(i => i.RelativePath).Should().BeEquivalentTo(["objects/a.txt", "objects/c.txt"],
            "the tolerated item must not be reported as deleted, but the others - including ones after it in " +
            "deepest-first order - still are");
        mets.PhysicalDivsByPath.Should().ContainKey("objects/b.txt",
            "it was never actually deleted, so its METS entry must remain");
    }

    /// <summary>
    /// Issue #259: the built-in protection guards (objects directory, metadata/ad-hoc, protected
    /// root files) must stay fatal even when ContinueIfFail lists the path - tolerance is for
    /// expected-absent/S3 failures, not for requests that should never be honoured at all.
    /// </summary>
    [Fact]
    public async Task Handle_Never_Tolerates_A_Protection_Guard_Failure_Even_When_The_Path_Is_Listed()
    {
        // "metadata/ad-hoc", not "objects": the objects guard only fires for a directory item whose
        // own RelativePath has no "/", and such items never reach this loop at all (they are
        // root-level entries, handled - if at all - by the DeleteFromRoot branch above, which is
        // file-only). metadata/ad-hoc's path does contain "/", so it is genuinely reachable here.
        var depositAdHoc = new WorkingDirectory { LocalPath = "metadata/ad-hoc", Name = "ad-hoc" };
        var depositMetadata = new WorkingDirectory { LocalPath = "metadata", Name = "metadata" };
        depositMetadata.Directories.Add(depositAdHoc);
        var depositRoot = new WorkingDirectory { LocalPath = "", Name = WorkingDirectory.DefaultRootName };
        depositRoot.Directories.Add(depositMetadata);
        var combinedRoot = CombinedBuilder.Build(depositRoot, null);

        var selection = new DeleteSelection
        {
            DeleteFromMets = false,
            DeleteFromDepositFiles = true,
            ContinueIfFail = ["metadata/ad-hoc"],
            Items =
            [
                new MinimalItem { RelativePath = "metadata/ad-hoc", IsDirectory = true, Whereabouts = Whereabouts.Deposit }
            ]
        };

        var request = new DeleteItems(
            isBagItLayout: false,
            depositFiles: DepositFiles,
            deleteSelection: selection,
            combinedRootDirectory: combinedRoot,
            depositETag: DepositETag);

        var result = await handler.Handle(request, CancellationToken.None);

        result.Failure.Should().BeTrue("the metadata/ad-hoc guard must stay fatal even when ContinueIfFail lists it");
        result.ErrorMessage.Should().Contain("ad-hoc");
    }

    [Fact]
    public async Task Handle_WritesMetsOnce_WhenAllDeletesSucceed()
    {
        var (combinedRoot, mets) = BuildFixture("a.txt", "b.txt");
        SetUpS3Delete("a.txt", succeeds: true);
        SetUpS3Delete("b.txt", succeeds: true);
        SetUpMetsManager(mets);

        A.CallTo(() => metsManager.WriteMets(A<FullMets>._)).Returns(Result.Ok());

        var request = new DeleteItems(
            isBagItLayout: false,
            depositFiles: DepositFiles,
            deleteSelection: SelectionFor("a.txt", "b.txt"),
            combinedRootDirectory: combinedRoot,
            depositETag: DepositETag);

        var result = await handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        A.CallTo(() => metsManager.WriteMets(A<FullMets>._)).MustHaveHappenedOnceExactly();
        mets.PhysicalDivsByPath.Should().BeEmpty();
    }
}
