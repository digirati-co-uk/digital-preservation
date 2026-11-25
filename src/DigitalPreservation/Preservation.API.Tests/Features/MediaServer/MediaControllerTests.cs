using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using DigitalPreservation.Workspace.Requests;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.MediaServer;
using Preservation.API.IIIF;
using Storage.Repository.Common;

namespace Preservation.API.Tests.Features.MediaServer;

/// <summary>
/// ResolveDepositFile is the only place MediaController builds an S3 URI (see MediaPathValidationTests
/// for its path-string gate). This exercises the controller itself: a requested path must resolve
/// against the deposit's tree before GetFileStream/GetRangedFileStream is ever called, so a future
/// branch that skipped the resolver would fail one of the "not in tree" cases here.
/// </summary>
public class MediaControllerTests
{
    private const string Token = "tok-1";
    private const string SourceId = "abc";
    private const string Bucket = "bucket";
    private static readonly Uri DepositFiles = new($"s3://{Bucket}/deposits/{SourceId}/");

    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly IMetsParser metsParser = A.Fake<IMetsParser>();
    private readonly ITokenService tokenService = A.Fake<ITokenService>();
    private readonly byte[] fileBytes = "0123456789"u8.ToArray();

    public MediaControllerTests()
    {
        A.CallTo(() => tokenService.IsValidForDeposit(Token, SourceId)).Returns(true);

        var deposit = new Deposit { Files = DepositFiles };
        A.CallTo(() => mediator.Send(A<GetDeposit>.That.Matches(r => r.Id == SourceId), A<CancellationToken>._))
            .Returns(Result.Ok(deposit));

        A.CallTo(() => mediator.Send(A<GetWorkingDirectory>._, A<CancellationToken>._))
            .Returns(Result.Ok(BuildTree()));

        A.CallTo(() => mediator.Send(A<GetFileStream>._, A<CancellationToken>._))
            .ReturnsLazily(() => Result.OkNotNull<(Stream?, DateTime)>((new MemoryStream(fileBytes), DateTime.UtcNow)));

        A.CallTo(() => mediator.Send(A<GetRangedFileStream>._, A<CancellationToken>._))
            .ReturnsLazily((IRequest<Result<RangedStreamResult?>> req, CancellationToken _) =>
            {
                var request = (GetRangedFileStream)req;
                var length = (request.To ?? (request.From + fileBytes.LongLength - 1)) - request.From + 1;
                var slice = fileBytes.Skip((int)request.From).Take((int)length).ToArray();
                return Result.Ok(new RangedStreamResult(new MemoryStream(slice), slice.LongLength));
            });
    }

    [Fact]
    public async Task A_File_In_The_Tree_Is_Streamed_From_Its_Own_S3_Uri()
    {
        var controller = CreateController("objects/page-001.jpg");

        var result = await controller.GetMedia(Token, "deposit", SourceId, "file", "objects/page-001.jpg");

        result.Should().BeOfType<FileStreamResult>();
        var expectedUri = new Uri($"s3://{Bucket}/deposits/{SourceId}/objects/page-001.jpg");
        A.CallTo(() => mediator.Send(A<GetFileStream>.That.Matches(r => r.FileUri == expectedUri), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => mediator.Send(A<GetFileStream>.That.Matches(r => r.FileUri != expectedUri), A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => mediator.Send(A<GetRangedFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("objects/not-there.jpg")]
    [InlineData("objects/sub/nope.jpg")]
    public async Task A_File_Not_In_The_Tree_Is_404(string missingPath)
    {
        var controller = CreateController(missingPath);

        var result = await controller.GetMedia(Token, "deposit", SourceId, "file", missingPath);

        // This is the assertion that catches a future branch built without the ResolveDepositFile gate.
        result.Should().BeOfType<NotFoundResult>();
        AssertNoStreamCalls();
    }

    [Theory]
    [InlineData("imagesvc", "objects/page-001.jpg/info.json")]
    [InlineData("imagesvc", "objects/page-001.jpg/full/100,/0/default.jpg")]
    [InlineData("imagesvc", "objects/page-001.jpg")]
    [InlineData("video", "objects/page-001.jpg")]
    [InlineData("audio", "objects/page-001.jpg")]
    public async Task The_Retired_Request_Types_Are_404_For_A_File_In_The_Tree(string type, string localPath)
    {
        // The Manifest paints only placeholders now (issue #261), so the media server answers for
        // the placeholders and the originals and nothing else.
        var controller = CreateController(localPath, type);

        var result = await controller.GetMedia(Token, "deposit", SourceId, type, localPath);

        result.Should().BeOfType<NotFoundResult>();
        AssertNoStreamCalls();
    }

    [Theory]
    [InlineData("canvas.png")]
    [InlineData("thumb.png")]
    public async Task A_Placeholder_Is_A_Png_Served_Without_Reading_The_Deposit(string name)
    {
        var controller = CreateController(name, "placeholder");

        var result = await controller.GetMedia(Token, "deposit", SourceId, "placeholder", name);

        result.Should().BeOfType<FileContentResult>().Which.ContentType.Should().Be("image/png");
        AssertNoStreamCalls();
    }

    [Fact]
    public async Task PlainFile_Branch_Uses_A_Ranged_Stream_For_A_Range_Request()
    {
        var controller = CreateController("objects/page-001.jpg");
        controller.ControllerContext.HttpContext.Request.Headers.Range = "bytes=0-4";

        var result = await controller.GetMedia(Token, "deposit", SourceId, "file", "objects/page-001.jpg");

        result.Should().BeOfType<FileStreamResult>();
        controller.ControllerContext.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);

        var expectedUri = new Uri($"s3://{Bucket}/deposits/{SourceId}/objects/page-001.jpg");
        A.CallTo(() => mediator.Send(
                A<GetRangedFileStream>.That.Matches(r => r.FileUri == expectedUri && r.From == 0 && r.To == 4),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => mediator.Send(A<GetFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task BagIt_Layout_Resolves_Under_The_Data_Prefix_And_Still_404s_For_Unknown_Paths()
    {
        A.CallTo(() => mediator.Send(A<GetWorkingDirectory>._, A<CancellationToken>._))
            .Returns(Result.Ok(BuildTree(bagIt: true)));

        var controller = CreateController("objects/page-001.jpg");
        var result = await controller.GetMedia(Token, "deposit", SourceId, "file", "objects/page-001.jpg");

        result.Should().BeOfType<FileStreamResult>();
        var expectedUri = new Uri($"s3://{Bucket}/deposits/{SourceId}/data/objects/page-001.jpg");
        A.CallTo(() => mediator.Send(A<GetFileStream>.That.Matches(r => r.FileUri == expectedUri), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        var missingController = CreateController("objects/not-there.jpg");
        var missingResult = await missingController.GetMedia(Token, "deposit", SourceId, "file", "objects/not-there.jpg");

        missingResult.Should().BeOfType<NotFoundResult>();
        // Still exactly the one call made above for the known file - the missing one made none.
        A.CallTo(() => mediator.Send(A<GetFileStream>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private void AssertNoStreamCalls()
    {
        A.CallTo(() => mediator.Send(A<GetFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => mediator.Send(A<GetRangedFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private MediaController CreateController(string localPath, string type = "file")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.org");
        httpContext.Request.Path = $"/media/{Token}/deposit/{SourceId}/{type}/{localPath}";

        return new MediaController(mediator, new WorkspaceManagerFactory(mediator, metsParser), tokenService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            Url = new UrlHelper(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()))
        };
    }

    private static WorkingDirectory BuildTree(bool bagIt = false)
    {
        string Prefixed(string path) => bagIt ? $"data/{path}" : path;

        var page1 = new WorkingFile
        {
            LocalPath = Prefixed("objects/page-001.jpg"),
            ContentType = "image/jpeg"
        };
        var page2 = new WorkingFile
        {
            LocalPath = Prefixed("objects/sub/page-002.jpg"),
            ContentType = "image/jpeg"
        };
        var subDirectory = new WorkingDirectory
        {
            LocalPath = Prefixed("objects/sub"),
            Files = [page2]
        };
        var objectsDirectory = new WorkingDirectory
        {
            LocalPath = Prefixed("objects"),
            Files = [page1],
            Directories = [subDirectory]
        };

        var root = new WorkingDirectory { LocalPath = string.Empty };
        if (bagIt)
        {
            root.Directories.Add(new WorkingDirectory { LocalPath = "data", Directories = [objectsDirectory] });
        }
        else
        {
            root.Directories.Add(objectsDirectory);
        }

        return root;
    }
}
