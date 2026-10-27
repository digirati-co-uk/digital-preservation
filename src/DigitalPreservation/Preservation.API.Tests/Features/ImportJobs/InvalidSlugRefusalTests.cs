using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using MediatR;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// A job whose ContainersToAdd or BinariesToAdd contains an invalid slug is refused with 400
/// before anything is queued or persisted (issue #298). Previously this was only checked
/// client-side, by the diff page; a caller posting a job directly was sent to Fedora unchecked.
/// </summary>
public class InvalidSlugRefusalTests
{
    [Fact]
    public async Task A_Job_Adding_A_Binary_With_An_Invalid_Slug_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        // '@' is legal in a URI path segment, so it survives Uri construction unchanged (unlike a
        // genuinely malformed '%' escape, which Uri itself normalises to a well-formed one), but it
        // is not a valid slug character - reliably exercising the same refusal a%zz.txt would.
        job.BinariesToAdd.Add(Binary("objects/report@final.txt"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not allowed in slugs");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_Adding_A_Container_With_An_Invalid_Slug_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.ContainersToAdd.Add(Container("objects/folder@name"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not allowed in slugs");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_Adding_A_Properly_Escaped_Name_Is_Not_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToAdd.Add(Binary("objects/Teto%20in%20tree.png"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Job_That_Only_Patches_An_Item_With_An_Invalid_Slug_Is_Not_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToPatch.Add(Binary("objects/report@final.txt"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Job_That_Only_Deletes_An_Item_With_An_Invalid_Slug_Is_Not_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToDelete.Add(Binary("objects/report@final.txt"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    private static ImportJob PlainJob() => new() { ArchivalGroup = ArchivalGroup, Deposit = DepositUri };
}
