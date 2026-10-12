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
/// A <c>depositName</c>/<c>DepositId</c> of ".." is, character by character, a valid slug -
/// <see cref="PreservedResource.ValidSlug"/> checks each character in isolation and does not
/// special-case a dots-only segment - so the POST /Pipeline slug check does not refuse it (see
/// <see cref="PipelineControllerTests"/> for the equivalent gap on the GET diagnostics endpoint).
/// </summary>
/// <remarks>
/// Two independent things still stop ".." from reaching a filesystem delete once it is queued:
/// no deposit is ever literally named "..", so <see cref="ProcessPipelineJobHandler.Handle"/>'s
/// deposit lookup fails and the handler returns before it ever enters the try/finally that calls
/// the clean-up methods (pinned here); and even if that lookup somehow succeeded, the clean-up
/// methods' own <c>PathX.IsUnderRoot</c> guard would refuse to delete outside the process folder
/// (pinned separately in <see cref="PathXTests"/>). This test exists so a future change that
/// quietly removes the first layer does not go unnoticed - PR #279 review comment.
/// </remarks>
public class ExecutePipelineJobDotsDepositIdTests
{
    private const string JobId = "job-1";
    private const string DepositId = "..";

    private readonly IPreservationApiClient preservationApiClient = A.Fake<IPreservationApiClient>();

    private ProcessPipelineJobHandler CreateHandler() =>
        new(NullLogger<ProcessPipelineJobHandler>.Instance,
            Options.Create(new StorageOptions()),
            Options.Create(new PipelineToolOptions()),
            new WorkspaceManagerFactory(A.Fake<IMediator>(), A.Fake<IMetsParser>()),
            preservationApiClient);

    [Fact]
    public async Task A_Dots_Only_DepositId_Is_Refused_Because_No_Such_Deposit_Is_Ever_Found()
    {
        A.CallTo(() => preservationApiClient.LogPipelineRunStatus(
                A<PipelineDeposit>.That.Matches(d => d.Status == PipelineJobStates.Running),
                A<CancellationToken>._))
            .Returns(Result.OkNotNull(new LogPipelineStatusResult { Status = PipelineJobStates.Running }));

        A.CallTo(() => preservationApiClient.GetDeposit(DepositId, A<CancellationToken>._))
            .Returns(Result.FailNotNull<Deposit?>(ErrorCodes.NotFound, "No deposit found"));

        var result = await CreateHandler().Handle(
            new ExecutePipelineJob(JobId, DepositId, "tester"), CancellationToken.None);

        result.Failure.Should().BeTrue();

        // The lock is only ever taken once a real deposit has been resolved - it never having been
        // released is the observable evidence that Handle() stopped at the failed deposit lookup,
        // before entering the try/finally that would (harmlessly - see PathXTests) attempt clean-up.
        A.CallTo(() => preservationApiClient.ReleaseDepositLock(A<Deposit>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
