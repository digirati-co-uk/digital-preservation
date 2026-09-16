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

    [Fact]
    public async Task A_Job_Whose_Deposit_Cannot_Be_Resolved_Is_Recorded_As_Failed_Not_Left_Running()
    {
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(A<PipelineDeposit>._, A<CancellationToken>._))
            .ReturnsLazily((PipelineDeposit d, CancellationToken _) =>
                Result.OkNotNull(new LogPipelineStatusResult { Status = d.Status }));
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Returns(Result.FailNotNull<Deposit?>(ErrorCodes.NotFound, "No deposit found"));

        var result = await CreateHandler().Handle(
            new ExecutePipelineJob(JobId, DepositId, "tester"), CancellationToken.None);

        result.Failure.Should().BeTrue();
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task A_Job_Whose_Workspace_Resolution_Throws_Is_Recorded_As_Failed_Not_Left_Running()
    {
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(A<PipelineDeposit>._, A<CancellationToken>._))
            .ReturnsLazily((PipelineDeposit d, CancellationToken _) =>
                Result.OkNotNull(new LogPipelineStatusResult { Status = d.Status }));
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Throws(new NotSupportedException("mets:FLocat xlink:href '../x' contains a dot segment"));

        var act = () => CreateHandler().Handle(new ExecutePipelineJob(JobId, DepositId, "tester"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.CompletedWithErrors),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
