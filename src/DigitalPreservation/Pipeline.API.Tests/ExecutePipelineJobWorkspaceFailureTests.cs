using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using FakeItEasy;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline.Requests;
using Preservation.Client;
using Xunit;

namespace Pipeline.API.Tests;

/// <summary>
/// The handler claims the job as Running before it resolves the deposit's workspace. If that
/// resolution then fails - the deposit is not found, or its METS is one the parser refuses - the
/// job must be recorded as completedWithErrors, not left Running for ever with nothing against it,
/// and the deposit's lock (taken when the run was requested) must be released whenever the deposit
/// itself is still reachable. The queue message is already gone by then, so nothing else will come
/// back to finish it.
/// </summary>
public class ExecutePipelineJobWorkspaceFailureTests
{
    private const string JobId = "job-1";
    private const string DepositId = "abc123def456";

    private readonly IPreservationApiClient preservationApiClient = A.Fake<IPreservationApiClient>();

    private ProcessPipelineJobHandler CreateHandler(IMediator? workspaceMediator = null) =>
        new(NullLogger<ProcessPipelineJobHandler>.Instance,
            Options.Create(new StorageOptions()),
            Options.Create(new PipelineToolOptions()),
            new WorkspaceManagerFactory(workspaceMediator ?? A.Fake<IMediator>(), A.Fake<IMetsParser>()),
            preservationApiClient);

    private void StatusUpdatesSucceed() =>
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(A<PipelineDeposit>._, A<CancellationToken>._))
            .ReturnsLazily((PipelineDeposit d, CancellationToken _) =>
                Result.OkNotNull(new LogPipelineStatusResult { Status = d.Status ?? PipelineJobStates.Running }));

    private static ExecutePipelineJob Request() => new(JobId, DepositId, "tester");

    [Fact]
    public async Task A_Job_Whose_Deposit_Cannot_Be_Resolved_Is_Recorded_As_Failed_After_Running_Not_Left_Running()
    {
        StatusUpdatesSucceed();
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Returns(Result.FailNotNull<Deposit?>(ErrorCodes.NotFound, "No deposit found"));

        var result = await CreateHandler().Handle(Request(), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        // The order is the behaviour: claimed as Running first, then recorded as failed, and nothing after.
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.Running), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                    A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors), A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(A<PipelineDeposit>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
        // A deposit that cannot be fetched cannot be unlocked from here - there is nothing to
        // unlock - and the fetch that already failed is not repeated just to fail again.
        A.CallTo(() => preservationApiClient.ReleaseDepositLock(A<Deposit>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Resolution_Failure_After_The_Deposit_Was_Fetched_Still_Releases_The_Lock()
    {
        // The METS-refusal shape: the deposit exists and is locked (RunPipeline locked it before
        // this handler ran), but its workspace cannot be built. The deposit is unlocked - no later
        // exit path will ever run for this job - and the failure recorded, in THAT order: the
        // moment completedWithErrors is visible a caller may retry the run, and a release landing
        // after that retry would strip the lock from under the new job. The release reuses the
        // deposit resolution already fetched: no second GetDeposit to fail at the worst moment.
        StatusUpdatesSucceed();
        var deposit = new Deposit { Id = new Uri("https://preservation.test/deposits/" + DepositId) };
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Returns(Result.OkNotNull<Deposit?>(deposit));
        A.CallTo(() => preservationApiClient.ReleaseDepositLock(A<Deposit>._, A<CancellationToken>._))
            .Returns(Result.Ok());
        var workspaceMediator = A.Fake<IMediator>();
        A.CallTo(workspaceMediator).Throws(new NotSupportedException("METS the parser refuses"));

        var result = await CreateHandler(workspaceMediator).Handle(Request(), CancellationToken.None);

        result.Failure.Should().BeTrue();
        A.CallTo(() => preservationApiClient.ReleaseDepositLock(deposit, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                    A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors), A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Lock_Release_Failure_Is_Swallowed_And_The_Failure_Still_Recorded()
    {
        // Best effort means best effort: a throw from the release must not displace the original
        // failure, must not escape the handler, and must not prevent the job's failure being
        // recorded (the release runs first).
        StatusUpdatesSucceed();
        var deposit = new Deposit { Id = new Uri("https://preservation.test/deposits/" + DepositId) };
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Returns(Result.OkNotNull<Deposit?>(deposit));
        A.CallTo(() => preservationApiClient.ReleaseDepositLock(A<Deposit>._, A<CancellationToken>._))
            .Throws(new HttpRequestException("preservation api away"));
        var workspaceMediator = A.Fake<IMediator>();
        A.CallTo(workspaceMediator).Throws(new NotSupportedException("METS the parser refuses"));

        var act = () => CreateHandler(workspaceMediator).Handle(Request(), CancellationToken.None);

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Failure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("could not be resolved");
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Job_Whose_Workspace_Resolution_Throws_Is_Recorded_As_Failed_With_A_Controlled_Message()
    {
        StatusUpdatesSucceed();
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Throws(new NotSupportedException("mets:FLocat xlink:href '../x' contains a dot segment; /internal/mount/path"));

        var result = await CreateHandler().Handle(Request(), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.UnknownError);
        // The exception's own text - which may carry internal paths - is logged, not recorded or returned.
        result.ErrorMessage.Should().Contain("could not be resolved").And.NotContain("/internal/mount/path");
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Cancellation_Is_Not_Converted_Into_A_Failed_Job()
    {
        StatusUpdatesSucceed();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Throws(new OperationCanceledException(cancellation.Token));

        var act = () => CreateHandler().Handle(Request(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors), A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task If_The_Failure_Cannot_Be_Recorded_The_Original_Failure_Is_Still_Returned()
    {
        // Best effort: with the Preservation API unreachable the job does stay Running (nothing
        // else can fix that), but the handler must not throw or replace the real failure with the
        // recording failure.
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.Running), A<CancellationToken>._))
            .Returns(Result.OkNotNull(new LogPipelineStatusResult { Status = PipelineJobStates.Running }));
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors), A<CancellationToken>._))
            .Throws(new HttpRequestException("preservation api away"));
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Returns(Result.FailNotNull<Deposit?>(ErrorCodes.NotFound, "No deposit found"));

        var act = () => CreateHandler().Handle(Request(), CancellationToken.None);

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("could not find the deposit");
    }
}
