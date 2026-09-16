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
/// job must be recorded as completedWithErrors, not left Running for ever with nothing against it.
/// The queue message is already gone by then, so nothing else will come back to finish it.
/// </summary>
public class ExecutePipelineJobWorkspaceFailureTests
{
    private const string JobId = "job-1";
    private const string DepositId = "abc123def456";

    private readonly IPreservationApiClient preservationApiClient = A.Fake<IPreservationApiClient>();

    private ProcessPipelineJobHandler CreateHandler() =>
        new(NullLogger<ProcessPipelineJobHandler>.Instance,
            Options.Create(new StorageOptions()),
            Options.Create(new PipelineToolOptions()),
            new WorkspaceManagerFactory(A.Fake<IMediator>(), A.Fake<IMetsParser>()),
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
