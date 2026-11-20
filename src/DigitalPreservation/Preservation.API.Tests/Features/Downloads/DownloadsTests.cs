using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Features.Downloads;
using Storage.Client;

namespace Preservation.API.Tests.Features.Downloads;

/// <summary>
/// Requests to download preserved files (RFC 011): who may ask, what is recorded, and how a
/// request's status follows its copy.
/// </summary>
public class DownloadsTests
{
    private const string Path = "cc/thing/objects/photo.tiff";

    private readonly IStorageApiClient storage = A.Fake<IStorageApiClient>();
    private readonly PreservationContext dbContext = new(new DbContextOptionsBuilder<PreservationContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public DownloadsTests()
    {
        A.CallTo(() => storage.GetResource("/repository/" + Path)).Returns(Result.Ok<PreservedResource?>(new Binary
        {
            Id = new Uri("https://storage.test/repository/" + Path),
            Name = "photo.tiff",
            PartOf = new Uri("https://storage.test/repository/cc/thing"),
            Digest = "a3f1c2",
            Size = 1234,
            ContentType = "image/tiff"
        }));
        A.CallTo(() => storage.StartDownloadCopy(A<DownloadCopy>._, A<CancellationToken>._)).Returns(Result.Ok());
        A.CallTo(() => storage.DeleteDownloadCopy(A<string>._, A<CancellationToken>._)).Returns(Result.Ok());
        CopyIsReady(false);
    }

    [Fact]
    public void Nobody_May_Download_When_No_Roles_Are_Configured()
    {
        new DownloadOptions().Permits(CallerWithRoles("Content.Download")).Should().BeFalse();
    }

    [Fact]
    public void Any_Caller_May_Download_When_The_Setting_Says_So()
    {
        new DownloadOptions { Roles = [DownloadOptions.AnyCaller] }.Permits(CallerWithRoles()).Should().BeTrue();
    }

    [Fact]
    public void Only_A_Caller_With_A_Configured_Role_May_Download()
    {
        var options = new DownloadOptions { Roles = ["Content.Download"] };

        options.Permits(CallerWithRoles("Content.Download")).Should().BeTrue();
        options.Permits(CallerWithRoles("Preservation.Call")).Should().BeFalse();
        options.Permits(CallerWithRoles()).Should().BeFalse();
    }

    [Fact]
    public async Task A_Request_Records_Who_Asked_For_What_And_Why_And_Starts_The_Copy()
    {
        var result = await Downloads().Request(new NewDownload { Path = Path, Reason = " For the exhibition " }, "tom", CancellationToken.None);

        var download = result.Value!;
        download.Status.Should().Be(DownloadStatus.Copying);
        download.RequestedBy.Should().Be("tom");
        download.Reason.Should().Be("For the exhibition");
        download.Path.Should().Be(Path);
        download.Digest.Should().Be("a3f1c2");
        download.Size.Should().Be(1234);
        download.Expires.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        (await dbContext.DownloadRequests.SingleAsync()).Id.Should().Be(download.Id);
        A.CallTo(() => storage.StartDownloadCopy(A<DownloadCopy>.That.Matches(copy =>
                copy.Id == download.Id
                && copy.ArchivalGroup == new Uri("https://storage.test/repository/cc/thing")
                && copy.Digest == "a3f1c2"
                && copy.Name == "photo.tiff"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Request_For_Something_That_Is_Not_A_File_Is_Not_Found_And_Not_Recorded()
    {
        A.CallTo(() => storage.GetResource("/repository/cc/thing"))
            .Returns(Result.Ok<PreservedResource?>(new Container { Id = new Uri("https://storage.test/repository/cc/thing") }));

        var result = await Downloads().Request(new NewDownload { Path = "cc/thing" }, "tom", CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        (await dbContext.DownloadRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_Request_Is_Not_Recorded_When_The_Copy_Cannot_Be_Started()
    {
        A.CallTo(() => storage.StartDownloadCopy(A<DownloadCopy>._, A<CancellationToken>._))
            .Returns(Result.Fail(ErrorCodes.Unprocessable, "no downloads bucket"));

        var result = await Downloads().Request(new NewDownload { Path = Path }, "tom", CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.Unprocessable);
        (await dbContext.DownloadRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_Request_Becomes_Ready_When_Its_Copy_Is_There()
    {
        var id = await ARequest();
        (await Downloads().Get(id, CancellationToken.None)).Value!.Status.Should().Be(DownloadStatus.Copying);

        CopyIsReady(true);

        (await Downloads().Get(id, CancellationToken.None)).Value!.Status.Should().Be(DownloadStatus.Ready);
    }

    [Fact]
    public async Task A_Request_Whose_Copy_Never_Appears_Fails_After_The_Timeout()
    {
        var id = await ARequest();
        await Change(id, request => request.Created = DateTime.UtcNow.AddMinutes(-16));

        var download = (await Downloads().Get(id, CancellationToken.None)).Value!;

        download.Status.Should().Be(DownloadStatus.Failed);
        download.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_Link_Is_Issued_Only_While_Ready_And_Each_One_Is_Recorded()
    {
        var id = await ARequest();
        var tooSoon = await Downloads().IssueLink(id, "tom", CancellationToken.None);
        tooSoon.ErrorCode.Should().Be(ErrorCodes.Conflict);
        tooSoon.ErrorMessage.Should().Be("The copy of photo.tiff is still being made. Try again shortly.");

        CopyIsReady(true);
        A.CallTo(() => storage.GetDownloadLink(id, A<CancellationToken>._)).Returns(Result.OkNotNull(new DownloadLink
        {
            Url = new Uri("https://downloads.test/signed"), Expires = DateTime.UtcNow.AddMinutes(2)
        }));

        (await Downloads().IssueLink(id, "tom", CancellationToken.None)).Value!.Url.Should().Be(new Uri("https://downloads.test/signed"));
        await Downloads().IssueLink(id, "brian", CancellationToken.None);

        (await Downloads().Get(id, CancellationToken.None)).Value!.LinksIssued.Should().Be(2);
        (await dbContext.Set<Preservation.API.Data.Entities.DownloadLinkIssued>().Select(link => link.IssuedTo).ToListAsync())
            .Should().BeEquivalentTo("tom", "brian");
    }

    [Fact]
    public async Task An_Expired_Request_Has_Its_Copy_Removed_When_Next_Looked_At()
    {
        var id = await ARequest();
        CopyIsReady(true);
        await Change(id, request => request.Expires = DateTime.UtcNow.AddMinutes(-1));

        var listed = await Downloads().List(CancellationToken.None);

        listed.Single().Status.Should().Be(DownloadStatus.Expired);
        A.CallTo(() => storage.DeleteDownloadCopy(id, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        (await Downloads().IssueLink(id, "tom", CancellationToken.None)).ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Deleting_Removes_The_Copy_And_Keeps_The_Record()
    {
        var id = await ARequest();

        (await Downloads().Delete(id, CancellationToken.None)).Success.Should().BeTrue();

        A.CallTo(() => storage.DeleteDownloadCopy(id, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        (await Downloads().Get(id, CancellationToken.None)).Value!.Status.Should().Be(DownloadStatus.Deleted);
        (await Downloads().IssueLink(id, "tom", CancellationToken.None)).ErrorMessage.Should()
            .Be("The copy of photo.tiff has been deleted. Request the file again to download it.");
    }

    private Preservation.API.Features.Downloads.Downloads Downloads() =>
        new(dbContext, storage, new IdentityMinter(), Options.Create(new DownloadOptions()));

    private async Task<string> ARequest() =>
        (await Downloads().Request(new NewDownload { Path = Path }, "tom", CancellationToken.None)).Value!.Id;

    private async Task Change(string id, Action<Preservation.API.Data.Entities.DownloadRequest> change)
    {
        change(await dbContext.DownloadRequests.SingleAsync(request => request.Id == id));
        await dbContext.SaveChangesAsync();
    }

    private void CopyIsReady(bool ready) =>
        A.CallTo(() => storage.GetDownloadCopyStatus(A<string>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new DownloadCopyStatus { Ready = ready }));

    private static ClaimsPrincipal CallerWithRoles(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(role => new Claim("roles", role)), "test"));
}
