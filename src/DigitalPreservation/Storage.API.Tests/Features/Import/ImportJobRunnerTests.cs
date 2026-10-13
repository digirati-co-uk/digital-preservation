using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using FakeItEasy;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Requests;
using Storage.API.Features.Repository.Requests;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// The runner must leave the stored result in a terminal, inactive state whatever happens after
/// dequeue: an active result answers 409 to every later job for that Archival Group, and the queue
/// message is already deleted, so nothing will come back to finish it.
/// </summary>
public class ImportJobRunnerTests
{
    private const string JobId = "job-1";
    private static readonly Uri BadGroup = new("https://storage.test/repository/cc/a%2Fb");
    private static readonly Uri GoodGroup = new("https://storage.test/repository/cc/thing");
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public async Task A_Job_Refused_By_Validation_Is_Saved_Inactive_Without_Asking_Fedora_For_The_Refused_Path()
    {
        // A job stored before the path rule existed: the executor now refuses it (CompletedWithErrors)
        // and the runner must not then hand the same refused path to GetResourceFromFedora, which throws.
        var mediator = A.Fake<IMediator>();
        var store = Store(BadGroup);
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ImportJobResult
            {
                Id = new Uri("https://storage.test/import/results/" + JobId), ImportJob = new Uri("https://storage.test/import/" + JobId), ArchivalGroup = BadGroup,
                Status = ImportJobStates.CompletedWithErrors
            }));
        A.CallTo(() => mediator.Send(A<GetResourceFromFedora>._, A<CancellationToken>._))
            .Throws(new ArgumentException("would have thrown on the path"));

        await Runner(mediator, store).Execute(JobId, TestContext.Current.CancellationToken);

        A.CallTo(() => mediator.Send(A<GetResourceFromFedora>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => store.SaveFinalImportJobResult(JobId,
                A<ImportJobResult>.That.Matches(r => r.Status == ImportJobStates.CompletedWithErrors),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Job_Whose_Executor_Throws_Is_Saved_Inactive_With_The_Error()
    {
        var mediator = A.Fake<IMediator>();
        var store = Store(GoodGroup);
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));

        var act = () => Runner(mediator, store).Execute(JobId, default);

        await act.Should().NotThrowAsync();
        A.CallTo(() => store.SaveFinalImportJobResult(JobId,
                A<ImportJobResult>.That.Matches(r =>
                    r.Status == ImportJobStates.CompletedWithErrors
                    && r.DateFinished != null
                    && r.Errors != null && r.Errors.Any(e => e.Message!.Contains("boom"))),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Job_Whose_Version_Fetch_Throws_Is_Still_Saved_Inactive()
    {
        var mediator = A.Fake<IMediator>();
        var store = Store(GoodGroup);
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ImportJobResult
            {
                Id = new Uri("https://storage.test/import/results/" + JobId), ImportJob = new Uri("https://storage.test/import/" + JobId), ArchivalGroup = GoodGroup,
                Status = ImportJobStates.Completed
            }));
        A.CallTo(() => mediator.Send(A<GetResourceFromFedora>._, A<CancellationToken>._))
            .Throws(new HttpRequestException("fedora away"));

        await Runner(mediator, store).Execute(JobId, TestContext.Current.CancellationToken);

        A.CallTo(() => store.SaveFinalImportJobResult(JobId, A<ImportJobResult>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Issue #350: RecordNewVersion used to only log when the group could not be read at all, and
    /// never checked whether the version it did read had actually moved on - so a job that reported
    /// success without ever really committing (a transaction Fedora's own expiry rolled back, for
    /// one) was recorded as a plain, unqualified success.
    /// </summary>
    [Fact]
    public async Task A_Successful_Job_Whose_Version_Did_Not_Change_Is_Recorded_As_An_Error()
    {
        var mediator = A.Fake<IMediator>();
        var store = Store(GoodGroup);
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ImportJobResult
            {
                Id = new Uri("https://storage.test/import/results/" + JobId), ImportJob = new Uri("https://storage.test/import/" + JobId), ArchivalGroup = GoodGroup,
                Status = ImportJobStates.Completed,
                SourceVersion = "v1"
            }));
        A.CallTo(() => mediator.Send(A<GetResourceFromFedora>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull<PreservedResource?>(new ArchivalGroup
            {
                Id = GoodGroup,
                Version = new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }
            }));

        await Runner(mediator, store).Execute(JobId, TestContext.Current.CancellationToken);

        A.CallTo(() => store.SaveFinalImportJobResult(JobId,
                A<ImportJobResult>.That.Matches(r =>
                    r.Status == ImportJobStates.CompletedWithErrors
                    && r.NewVersion == "v1"
                    && r.Errors != null && r.Errors.Any(e => e.Message!.Contains("version did not change"))),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The new-group case has no SourceVersion to compare against - any version at all confirms
    /// the create landed, so this must not be flagged as the "version did not change" error.
    /// </summary>
    [Fact]
    public async Task A_Successful_Job_For_A_New_Group_With_No_Source_Version_Is_Not_Flagged()
    {
        var mediator = A.Fake<IMediator>();
        var store = Store(GoodGroup);
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ImportJobResult
            {
                Id = new Uri("https://storage.test/import/results/" + JobId), ImportJob = new Uri("https://storage.test/import/" + JobId), ArchivalGroup = GoodGroup,
                Status = ImportJobStates.Completed,
                SourceVersion = null
            }));
        A.CallTo(() => mediator.Send(A<GetResourceFromFedora>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull<PreservedResource?>(new ArchivalGroup
            {
                Id = GoodGroup,
                Version = new ObjectVersion { OcflVersion = "v1", MementoTimestamp = "t", MementoDateTime = DateTime.UtcNow }
            }));

        await Runner(mediator, store).Execute(JobId, TestContext.Current.CancellationToken);

        A.CallTo(() => store.SaveFinalImportJobResult(JobId,
                A<ImportJobResult>.That.Matches(r => r.Status == ImportJobStates.Completed),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private static ImportJobRunner Runner(IMediator mediator, IImportJobResultStore store) =>
        new(NullLogger<ImportJobRunner>.Instance, mediator, store, ScopeFactoryFor(store), EmptyConfiguration);

    private static IImportJobResultStore Store(Uri archivalGroup)
    {
        var store = A.Fake<IImportJobResultStore>();
        A.CallTo(() => store.GetImportJob(JobId, A<CancellationToken>._))
            .Returns(Result.OkNotNull<ImportJob?>(new ImportJob { ArchivalGroup = archivalGroup }));
        A.CallTo(() => store.GetImportJobResult(JobId, A<CancellationToken>._))
            .Returns(Result.OkNotNull<ImportJobResult?>(new ImportJobResult
            {
                Id = new Uri("https://storage.test/import/results/" + JobId), ImportJob = new Uri("https://storage.test/import/" + JobId), ArchivalGroup = archivalGroup,
                Status = ImportJobStates.Waiting
            }));
        A.CallTo(() => store.SaveImportJobResult(A<string>._, A<ImportJobResult>._, A<bool>._, A<bool>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        A.CallTo(() => store.SaveFinalImportJobResult(A<string>._, A<ImportJobResult>._, A<CancellationToken>._))
            .Returns(Result.Ok(true));
        A.CallTo(() => store.Heartbeat(A<string>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        return store;
    }

    /// <summary>
    /// The runner's heartbeat loop resolves its own IImportJobResultStore from a fresh DI scope
    /// (deliberately: it must not share the job's own DbContext). Faked here to resolve back to the
    /// same fake store these tests already configure, rather than exercising real DI.
    /// </summary>
    private static IServiceScopeFactory ScopeFactoryFor(IImportJobResultStore store)
    {
        var serviceProvider = A.Fake<IServiceProvider>();
        A.CallTo(() => serviceProvider.GetService(typeof(IImportJobResultStore))).Returns(store);

        var scope = A.Fake<IServiceScope>();
        A.CallTo(() => scope.ServiceProvider).Returns(serviceProvider);

        var scopeFactory = A.Fake<IServiceScopeFactory>();
        A.CallTo(() => scopeFactory.CreateScope()).Returns(scope);
        return scopeFactory;
    }
}
