using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using MediatR;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// Renaming (changing only a Container or Binary's display name, never its slug or path) is not
/// implemented yet, so a job that asks for one is refused with 400 before anything is queued or
/// persisted (issue #260).
/// </summary>
public class RenameRefusalTests
{
    [Fact]
    public async Task A_Job_With_A_Binary_Rename_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToRename.Add(Binary("objects/report.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("Renaming is not supported yet");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_A_Container_Rename_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.ContainersToRename.Add(Container("objects/folder"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("Renaming is not supported yet");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Diff_Reference_Whose_Generated_Diff_Contains_A_Rename_Is_Refused()
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

        var result = await controller.ExecuteImportJob(DepositId, diffReference, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("Renaming is not supported yet");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_No_Renames_Is_Unaffected()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToAdd.Add(Binary("objects/new.pdf"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Job_With_A_Rename_Gets_The_Rename_Message_Not_The_Suppression_One()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.SuppressActivityStreamEvent = true;
        job.BinariesToRename.Add(Binary("objects/report.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("Renaming is not supported yet");
        Executed(mediator).MustNotHaveHappened();
    }

    private static ImportJob PlainJob() => new() { ArchivalGroup = ArchivalGroup, Deposit = DepositUri };

    /// <summary>
    /// A controller whose Url.RouteUrl resolves to this deposit's diff job - GetDiffUri needs it to
    /// build the response for the "diff reference contains a rename" case - with the migration flag
    /// on. That matters for one test here: a suppressed job with a rename must get the rename
    /// refusal, not the suppression gate's "EnableMetsIdNormalisation" one, and the gate only
    /// refuses when the flag is off.
    /// </summary>
    private static ImportJobsController Controller(IMediator mediator) =>
        ImportJobsControllerHarness.Controller(
            mediator, migrationFlagOn: true, urlHelper: UrlHelperReturning(DepositUri + "/importjobs/diff"));
}
