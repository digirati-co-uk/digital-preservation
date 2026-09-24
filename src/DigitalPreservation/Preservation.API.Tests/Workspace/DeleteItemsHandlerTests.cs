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
        handler = new DeleteItemsHandler(s3Client, metsManager);
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
