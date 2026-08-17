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
/// The Conflict branch at the top of <see cref="ProcessPipelineJobHandler.Handle"/>: a pipeline job
/// is claimed exactly once, and a delivery that loses the claim is abandoned rather than run again
/// (issue #221).
/// </summary>
/// <remarks>
/// The rule this asserts is "abandon on Conflict, and <em>only</em> on Conflict", so both halves are
/// tested. The second half is the one that is easy to break by tidying: making the handler bail on
/// any failed status update looks like a simplification, and would silently drop jobs whenever
/// Preservation API had a transient blip — with nothing to retry them, because
/// <c>SqsPipelineQueue.DequeueRequest</c> deletes the message before this code runs.
/// </remarks>
public class ExecutePipelineJobClaimTests
{
    private const string JobId = "job-1";
    private const string DepositId = "deposit-1";

    private readonly IPreservationApiClient preservationApiClient = A.Fake<IPreservationApiClient>();

    private ProcessPipelineJobHandler CreateHandler() =>
        new(NullLogger<ProcessPipelineJobHandler>.Instance,
            Options.Create(new StorageOptions()),
            Options.Create(new PipelineToolOptions()),
            new WorkspaceManagerFactory(A.Fake<IMediator>(), A.Fake<IMetsParser>()),
            preservationApiClient);

    private void ClaimReturns(Result<LogPipelineStatusResult> result) =>
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.Running),
                A<CancellationToken>._))
            .Returns(result);

    [Fact]
    public async Task A_Delivery_That_Cannot_Claim_The_Job_Is_Abandoned()
    {
        ClaimReturns(Result.FailNotNull<LogPipelineStatusResult>(
            ErrorCodes.Conflict, "Pipeline job job-1 is not waiting to be run, so it cannot be started again."));

        var result = await CreateHandler().Handle(
            new ExecutePipelineJob(JobId, DepositId, "tester"), CancellationToken.None);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);

        // Abandoning means abandoning before doing any work: no deposit is read, so Brunnhilde is
        // never reached and no second virus-scan event can be written for a scan that happened once.
        A.CallTo(() => preservationApiClient.GetDeposit(A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData(ErrorCodes.UnknownError)]
    [InlineData(ErrorCodes.NotFound)]
    [InlineData(ErrorCodes.BadRequest)]
    public async Task A_Delivery_That_Fails_To_Start_For_Any_Other_Reason_Still_Runs(string errorCode)
    {
        ClaimReturns(Result.FailNotNull<LogPipelineStatusResult>(errorCode, "something else went wrong"));

        var result = await CreateHandler().Handle(
            new ExecutePipelineJob(JobId, DepositId, "tester"), CancellationToken.None);

        // It proceeds past the claim and tries to do the work. It gets no further here, because the
        // fake has no deposit to give it — but reaching that call at all is the assertion: the
        // handler did not treat a non-Conflict failure as a duplicate.
        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .MustHaveHappened();
        result.ErrorCode.Should().NotBe(ErrorCodes.Conflict);
    }
}
