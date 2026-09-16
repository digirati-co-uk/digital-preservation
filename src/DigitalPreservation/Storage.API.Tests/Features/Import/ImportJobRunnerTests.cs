using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using MediatR;
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

        await new ImportJobRunner(NullLogger<ImportJobRunner>.Instance, mediator, store).Execute(JobId, default);

        A.CallTo(() => mediator.Send(A<GetResourceFromFedora>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => store.SaveImportJobResult(JobId,
                A<ImportJobResult>.That.Matches(r => r.Status == ImportJobStates.CompletedWithErrors),
                false, true, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Job_Whose_Executor_Throws_Is_Saved_Inactive_With_The_Error()
    {
        var mediator = A.Fake<IMediator>();
        var store = Store(GoodGroup);
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));

        var act = () => new ImportJobRunner(NullLogger<ImportJobRunner>.Instance, mediator, store).Execute(JobId, default);

        await act.Should().NotThrowAsync();
        A.CallTo(() => store.SaveImportJobResult(JobId,
                A<ImportJobResult>.That.Matches(r =>
                    r.Status == ImportJobStates.CompletedWithErrors
                    && r.DateFinished != null
                    && r.Errors != null && r.Errors.Any(e => e.Message!.Contains("boom"))),
                false, true, A<CancellationToken>._))
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

        await new ImportJobRunner(NullLogger<ImportJobRunner>.Instance, mediator, store).Execute(JobId, default);

        A.CallTo(() => store.SaveImportJobResult(JobId, A<ImportJobResult>._, false, true, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

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
        return store;
    }
}
