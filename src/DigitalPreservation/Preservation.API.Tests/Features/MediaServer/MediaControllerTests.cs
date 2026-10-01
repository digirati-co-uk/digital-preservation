using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Storage.Repository.Common;

namespace Preservation.API.Tests.Features.MediaServer;

/// <summary>
/// ResolveDepositFile is the only place MediaController builds an S3 URI (see MediaPathValidationTests
/// for its path-string gate). This exercises the controller itself: every branch must resolve a
/// requested path against the deposit's tree before ever calling GetFileStream/GetRangedFileStream,
/// so a future branch that skipped the resolver would fail one of the "not in tree" cases here.
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
    private readonly byte[] jpegBytes = CreateJpegBytes();

    public enum MediaBranch
    {
        PlainFile,
        InfoJson,
        DefaultJpg,
        BareRedirect
    }

    public MediaControllerTests()
    {
        A.CallTo(() => tokenService.GetKey(Token)).Returns(SourceId);

        var deposit = new Deposit { Files = DepositFiles };
        A.CallTo(() => mediator.Send(A<GetDeposit>.That.Matches(r => r.Id == SourceId), A<CancellationToken>._))
            .Returns(Result.Ok(deposit));

        A.CallTo(() => mediator.Send(A<GetWorkingDirectory>._, A<CancellationToken>._))
            .Returns(Result.Ok(BuildTree()));

        A.CallTo(() => mediator.Send(A<GetFileStream>._, A<CancellationToken>._))
            .ReturnsLazily(() => Result.OkNotNull<(Stream?, DateTime)>((new MemoryStream(jpegBytes), DateTime.UtcNow)));

        A.CallTo(() => mediator.Send(A<GetRangedFileStream>._, A<CancellationToken>._))
            .ReturnsLazily((IRequest<Result<RangedStreamResult?>> req, CancellationToken _) =>
            {
                var request = (GetRangedFileStream)req;
                var length = (request.To ?? (request.From + jpegBytes.LongLength - 1)) - request.From + 1;
                var slice = jpegBytes.Skip((int)request.From).Take((int)length).ToArray();
                return Result.Ok(new RangedStreamResult(new MemoryStream(slice), slice.LongLength));
            });
    }

    // Four branches, most specific request shape first: plain file, info.json, default.jpg and the
    // bare imagesvc URL that redirects to info.json. A new branch means adding one case here.
    public static IEnumerable<object[]> Branches()
    {
        yield return [MediaBranch.PlainFile];
        yield return [MediaBranch.InfoJson];
        yield return [MediaBranch.DefaultJpg];
        yield return [MediaBranch.BareRedirect];
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public async Task Branch_Succeeds_For_A_Path_In_The_Tree(MediaBranch branch)
    {
        var (type, localPath) = RequestFor(branch, "objects/page-001.jpg");
        var controller = CreateController(localPath, type);

        var result = await controller.GetMedia(Token, "deposit", SourceId, type, localPath);

        var expectedUri = new Uri($"s3://{Bucket}/deposits/{SourceId}/objects/page-001.jpg");

        switch (branch)
        {
            case MediaBranch.PlainFile:
            case MediaBranch.DefaultJpg:
                result.Should().BeOfType<FileStreamResult>();
                AssertStreamedOnlyFrom(expectedUri);
                break;
            case MediaBranch.InfoJson:
                result.Should().BeOfType<ContentResult>();
                ((ContentResult)result).ContentType.Should().Be("application/json");
                AssertNoStreamCalls();
                break;
            case MediaBranch.BareRedirect:
                result.Should().BeOfType<RedirectResult>();
                AssertNoStreamCalls();
                break;
        }
    }

    [Theory]
    [InlineData(MediaBranch.PlainFile, "objects/not-there.jpg")]
    [InlineData(MediaBranch.PlainFile, "objects/sub/nope.jpg")]
    [InlineData(MediaBranch.InfoJson, "objects/not-there.jpg")]
    [InlineData(MediaBranch.InfoJson, "objects/sub/nope.jpg")]
    [InlineData(MediaBranch.DefaultJpg, "objects/not-there.jpg")]
    [InlineData(MediaBranch.DefaultJpg, "objects/sub/nope.jpg")]
    [InlineData(MediaBranch.BareRedirect, "objects/not-there.jpg")]
    [InlineData(MediaBranch.BareRedirect, "objects/sub/nope.jpg")]
    public async Task Branch_Returns_404_For_A_Path_Not_In_The_Tree(MediaBranch branch, string missingPath)
    {
        var (type, localPath) = RequestFor(branch, missingPath);
        var controller = CreateController(localPath, type);

        var result = await controller.GetMedia(Token, "deposit", SourceId, type, localPath);

        // This is the assertion that catches a future branch built without the ResolveDepositFile gate.
        result.Should().BeOfType<NotFoundResult>();
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

    private void AssertStreamedOnlyFrom(Uri expectedUri)
    {
        A.CallTo(() => mediator.Send(A<GetFileStream>.That.Matches(r => r.FileUri == expectedUri), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => mediator.Send(A<GetFileStream>.That.Matches(r => r.FileUri != expectedUri), A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => mediator.Send(A<GetRangedFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private void AssertNoStreamCalls()
    {
        A.CallTo(() => mediator.Send(A<GetFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => mediator.Send(A<GetRangedFileStream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private static (string Type, string LocalPath) RequestFor(MediaBranch branch, string resolvedPath) => branch switch
    {
        MediaBranch.PlainFile => ("file", resolvedPath),
        MediaBranch.InfoJson => ("imagesvc", $"{resolvedPath}/info.json"),
        MediaBranch.DefaultJpg => ("imagesvc", $"{resolvedPath}/full/100,/0/default.jpg"),
        // Same shape as default.jpg but a different last segment, so it misses the specific
        // full/{w,h}/0/default.jpg pattern and falls through to the bare-redirect branch.
        MediaBranch.BareRedirect => ("imagesvc", $"{resolvedPath}/full/100,/0/default.png"),
        _ => throw new ArgumentOutOfRangeException(nameof(branch))
    };

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
            ContentType = "image/jpeg",
            Metadata = [new ExtentMetadata { Source = "test", PixelWidth = 10, PixelHeight = 10 }]
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

    private static byte[] CreateJpegBytes()
    {
        using var image = new Image<Rgb24>(10, 10, new Rgb24(10, 20, 30));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }
}
