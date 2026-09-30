using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// A direct caller of the Storage API (the Preservation API always fills CreatedBy itself first)
/// could queue a job with lastModifiedBy but no createdBy: it passed the queue check, then
/// ExecuteImportJobHandler dereferenced the null CreatedBy and NullReferenceException'd. And the
/// missing-attribution refusal itself was a 401, which tells a client to refresh its token - useless
/// for a malformed request (issue #273 item 1).
/// </summary>
public class QueueImportJobHandlerTests
{
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");
    private static readonly Uri Agent = new("https://storage.test/agents/someone");

    [Fact]
    public async Task A_Missing_CreatedBy_Is_Defaulted_To_LastModifiedBy_Before_The_Job_Is_Saved()
    {
        var store = A.Fake<IImportJobResultStore>();
        A.CallTo(() => store.GetActiveJobsForArchivalGroup(ArchivalGroup, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new List<string>()));
        A.CallTo(() => store.SaveImportJob(A<string>._, A<ImportJob>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        A.CallTo(() => store.SaveImportJobResult(A<string>._, A<ImportJobResult>._, A<bool>._, A<bool>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        var identityMinter = A.Fake<IIdentityMinter>();
        A.CallTo(() => identityMinter.MintIdentity(A<string>._, A<Uri>._)).Returns("job-1");
        var handler = new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance, MakeConverters(), identityMinter, store, A.Fake<IImportJobQueue>());
        var importJob = new ImportJob { ArchivalGroup = ArchivalGroup, LastModifiedBy = Agent };

        var result = await handler.Handle(new QueueImportJob(importJob), CancellationToken.None);

        result.Success.Should().BeTrue();
        importJob.CreatedBy.Should().Be(Agent);
        A.CallTo(() => store.SaveImportJob(A<string>._,
                A<ImportJob>.That.Matches(j => j.CreatedBy == j.LastModifiedBy), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Missing_LastModifiedBy_Is_Refused_As_BadRequest_Not_Unauthorized()
    {
        var handler = new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance, MakeConverters(), A.Fake<IIdentityMinter>(),
            A.Fake<IImportJobResultStore>(), A.Fake<IImportJobQueue>());
        var importJob = new ImportJob { ArchivalGroup = ArchivalGroup };

        var result = await handler.Handle(new QueueImportJob(importJob), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest,
            "a missing attribution field is a malformed request, not a credential problem");
    }

    private static Converters MakeConverters() => new(
        Options.Create(new FedoraOptions
        {
            Root = new Uri("https://fedora.test/fcrepo/rest/"),
            AdminUser = "admin",
            AdminPassword = "admin",
            Bucket = "fedora-bucket",
            OcflS3Prefix = ""
        }),
        Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/") }));
}
