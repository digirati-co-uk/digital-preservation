using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;
using Storage.API.Tests.TestingInfrastructure;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// QueueImportJobHandler judges a job before saving or queuing anything:
/// <list type="bullet">
/// <item>PreProcessValidateImportJob runs first, so a job that fails it - a rename to an empty
/// name, among other things (issue #347) - is refused with 400 without ever touching the store or
/// the queue.</item>
/// <item>A direct caller of the Storage API (the Preservation API always fills CreatedBy itself
/// first) could queue a job with lastModifiedBy but no createdBy: it passed the queue check, then
/// ExecuteImportJobHandler dereferenced the null CreatedBy and NullReferenceException'd. And the
/// missing-attribution refusal itself was a 401, which tells a client to refresh its token - useless
/// for a malformed request (issue #273 item 1).</item>
/// </list>
/// </summary>
public class QueueImportJobHandlerTests
{
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");
    private static readonly Uri Agent = new("https://storage.test/agents/someone");

    private readonly IImportJobResultStore importJobResultStore = A.Fake<IImportJobResultStore>();
    private readonly IImportJobQueue importJobQueue = A.Fake<IImportJobQueue>();
    private readonly QueueImportJobHandler handler;

    public QueueImportJobHandlerTests()
    {
        handler = new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance,
            TestConverters.Make(storageRoot: new Uri("https://storage.test/repository/")),
            A.Fake<IIdentityMinter>(),
            importJobResultStore,
            importJobQueue);
    }

    [Fact]
    public async Task A_Job_With_A_Rename_To_An_Empty_Name_Is_Refused_And_Nothing_Is_Saved_Or_Queued()
    {
        var job = new ImportJob
        {
            ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
            Deposit = new Uri("https://preservation.test/deposits/dep-1")
        };
        job.BinariesToRename.Add(new Binary { Id = new Uri("https://storage.test/repository/cc/thing/objects/page-001.tif") });

        var result = await handler.Handle(new QueueImportJob(job), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("empty name");
        A.CallTo(importJobResultStore).MustNotHaveHappened();
        A.CallTo(importJobQueue).MustNotHaveHappened();
    }

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
        var queueHandler = new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance, TestConverters.Make(), identityMinter, store, A.Fake<IImportJobQueue>());
        var importJob = new ImportJob { ArchivalGroup = ArchivalGroup, LastModifiedBy = Agent };

        var result = await queueHandler.Handle(new QueueImportJob(importJob), CancellationToken.None);

        result.Success.Should().BeTrue();
        importJob.CreatedBy.Should().Be(Agent);
        A.CallTo(() => store.SaveImportJob(A<string>._,
                A<ImportJob>.That.Matches(j => j.CreatedBy == j.LastModifiedBy), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Missing_LastModifiedBy_Is_Refused_As_BadRequest_Not_Unauthorized()
    {
        var queueHandler = new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance, TestConverters.Make(), A.Fake<IIdentityMinter>(),
            A.Fake<IImportJobResultStore>(), A.Fake<IImportJobQueue>());
        var importJob = new ImportJob { ArchivalGroup = ArchivalGroup };

        var result = await queueHandler.Handle(new QueueImportJob(importJob), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest,
            "a missing attribution field is a malformed request, not a credential problem");
    }

    private (IImportJobResultStore Store, IImportJobQueue Queue, QueueImportJobHandler Handler) Accepting()
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
        var queue = A.Fake<IImportJobQueue>();
        return (store, queue, new QueueImportJobHandler(
            NullLogger<QueueImportJobHandler>.Instance, TestConverters.Make(), identityMinter, store, queue));
    }

    [Fact]
    public async Task A_Disconnect_After_The_Job_Is_Saved_Still_Saves_Its_Result_And_Queues_It()
    {
        // Issue #428: once the job row is written it counts as active for its Archival Group, so
        // abandoning it unqueued would block every later import for that group with a 409.
        var (store, queue, queueHandler) = Accepting();
        using var requestAborted = new CancellationTokenSource();
        A.CallTo(() => store.SaveImportJob(A<string>._, A<ImportJob>._, A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                requestAborted.Cancel();
                return Result.Ok();
            });
        var importJob = new ImportJob { ArchivalGroup = ArchivalGroup, LastModifiedBy = Agent };

        var result = await queueHandler.Handle(new QueueImportJob(importJob), requestAborted.Token);

        result.Success.Should().BeTrue();
        A.CallTo(() => store.SaveImportJob(A<string>._, A<ImportJob>._, A<CancellationToken>.That.Matches(t => !t.CanBeCanceled)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => store.SaveImportJobResult(A<string>._, A<ImportJobResult>._, A<bool>._, A<bool>._,
                A<CancellationToken>.That.Matches(t => !t.CanBeCanceled)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => queue.QueueRequest("job-1", A<CancellationToken>.That.Matches(t => !t.CanBeCanceled)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Disconnect_Before_Anything_Is_Saved_Cancels_Without_Saving_Or_Queuing()
    {
        var (store, queue, queueHandler) = Accepting();
        using var requestAborted = new CancellationTokenSource();
        await requestAborted.CancelAsync();
        var importJob = new ImportJob { ArchivalGroup = ArchivalGroup, LastModifiedBy = Agent };

        var act = () => queueHandler.Handle(new QueueImportJob(importJob), requestAborted.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        A.CallTo(() => store.SaveImportJob(A<string>._, A<ImportJob>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(queue).MustNotHaveHappened();
    }
}
