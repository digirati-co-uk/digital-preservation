using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using MediatR;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// Renaming (changing only a Container or Binary's display name, never its slug or path) is
/// implemented by the Storage API's executor (issue #347), so the Preservation API no longer
/// refuses a job that asks for one, as it did under #260.
/// </summary>
public class RenameAcceptanceTests
{
    [Fact]
    public async Task A_Job_With_A_Binary_Rename_Is_Executed()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToRename.Add(Binary("objects/report.pdf"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_A_Container_Rename_Is_Executed()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.ContainersToRename.Add(Container("objects/folder"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Diff_Reference_Whose_Generated_Diff_Contains_A_Rename_Is_Executed()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var diff = PlainJob();
        diff.ContainersToRename.Add(Container("objects/folder"));
        A.CallTo(() => mediator.Send(A<GetDiffImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(diff));
        var diffReference = new ImportJob
        {
            Id = new Uri(DepositUri + "/importjobs/diff"),
            Deposit = DepositUri,
            ArchivalGroup = ArchivalGroup
        };

        await controller.ExecuteImportJob(DepositId, diffReference, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Job_With_A_Rename_Is_Still_Refused_By_The_Suppression_Gate()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.SuppressActivityStreamEvent = true;
        job.BinariesToRename.Add(Binary("objects/report.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        // A rename changes what a reader sees, so it is not a METS-only migration.
        Refusal(result).Should().Contain("renames content");
        Executed(mediator).MustNotHaveHappened();
    }

    private static ImportJob PlainJob() => new() { ArchivalGroup = ArchivalGroup, Deposit = DepositUri };

    /// <summary>
    /// A controller whose Url.RouteUrl resolves to this deposit's diff job - GetDiffUri needs it to
    /// build the response for the diff-reference case - with the migration flag on, so a suppressed
    /// job reaches the suppression gate's "what for" check rather than the feature-flag refusal.
    /// </summary>
    private static ImportJobsController Controller(IMediator mediator) =>
        ImportJobsControllerHarness.Controller(
            mediator, migrationFlagOn: true, urlHelper: UrlHelperReturning(DepositUri + "/importjobs/diff"));
}
