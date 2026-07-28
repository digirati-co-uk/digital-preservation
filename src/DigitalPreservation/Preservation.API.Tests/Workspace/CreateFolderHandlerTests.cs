using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace.Requests;
using FakeItEasy;
using Storage.Repository.Common;

namespace Preservation.API.Tests.Workspace;

public class CreateFolderHandlerTests
{
    private readonly IAmazonS3 s3Client = A.Fake<IAmazonS3>();
    private readonly IStorage storage = A.Fake<IStorage>();
    private readonly IMetsManager metsManager = A.Fake<IMetsManager>();
    private readonly CreateFolderHandler handler;

    public CreateFolderHandlerTests()
    {
        handler = new CreateFolderHandler(s3Client, storage, metsManager);

        A.CallTo(() => s3Client.PutObjectAsync(A<PutObjectRequest>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.Created }));

        A.CallTo(() => s3Client.GetObjectMetadataAsync(A<GetObjectMetadataRequest>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(new GetObjectMetadataResponse { LastModified = DateTime.UtcNow }));

        A.CallTo(() => storage.AddToDepositFileSystem(A<Uri>.Ignored, A<WorkingDirectory>.Ignored, A<CancellationToken>.Ignored))
            .Returns(Task.FromResult(Result.OkNotNull(new WorkingDirectory { LocalPath = "", Name = "root", Modified = DateTime.UtcNow })));
    }

    // LPII-133: CreateFolderHandler used to discard HandleCreateFolder's result and always return
    // Result.Ok(dir) regardless, so an ETag mismatch (PreconditionFailed) on the METS write left the S3
    // folder marker created but the METS entry silently missing - the caller was told the call succeeded.
    [Fact]
    public async Task Handle_ReturnsFailure_WhenMetsWriteFails()
    {
        A.CallTo(() => metsManager.HandleCreateFolder(A<Uri>.Ignored, A<WorkingDirectory>.Ignored, A<string>.Ignored))
            .Returns(Task.FromResult(Result.Fail(ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS")));

        var request = new CreateFolder(
            isBagItLayout: false,
            rootUri: new Uri("s3://bucket/deposits/dep123/"),
            name: "metadata",
            newFolderSlug: "metadata",
            parent: null,
            metsETag: "stale-etag");

        var result = await handler.Handle(request, CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PreconditionFailed);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenMetsWriteSucceeds()
    {
        A.CallTo(() => metsManager.HandleCreateFolder(A<Uri>.Ignored, A<WorkingDirectory>.Ignored, A<string>.Ignored))
            .Returns(Task.FromResult(Result.Ok()));

        var request = new CreateFolder(
            isBagItLayout: false,
            rootUri: new Uri("s3://bucket/deposits/dep123/"),
            name: "metadata",
            newFolderSlug: "metadata",
            parent: null,
            metsETag: "current-etag");

        var result = await handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("metadata");
    }
}
