using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using MediatR;
using Preservation.API.Features.Deposits.Requests;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// suppressActivityStreamEvent keeps a preserved version out of the published Activity Stream,
/// which means IIIF is never told to rebuild. That is right for a METS ID migration and wrong for
/// almost anything else, so it is only honoured while the migration machinery it belongs to
/// (FeatureFlags:EnableMetsIdNormalisation) is switched on - and refused loudly otherwise, rather
/// than silently published, which also protects a newer client against an older API.
/// </summary>
public class SuppressionGateTests
{
    [Fact]
    public async Task Suppression_Is_Refused_When_The_Migration_Flag_Is_Off()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: false);

        var result = await controller.ExecuteImportJob(DepositId, new ImportJob { SuppressActivityStreamEvent = true }, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("EnableMetsIdNormalisation");
        // Refused before anything was looked up, let alone executed.
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task An_Unsuppressed_Job_Is_Unaffected_By_The_Flag()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: false);

        await controller.ExecuteImportJob(DepositId, MetsOnlyJob(suppress: false), TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Mets_Only_Patch_Passes_The_Gate_When_The_Flag_Is_On()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);

        await controller.ExecuteImportJob(DepositId, MetsOnlyJob(suppress: true), TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Job_That_Changes_Content_Is_Refused_Even_With_The_Flag_On()
    {
        // The flag says WHEN suppression may be used; this says WHAT FOR. A suppressed content
        // change would preserve a real new version that IIIF is never told to rebuild from - and
        // until now the only thing preventing it was the migration tool's client-side check.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.BinariesToAdd.Add(Binary("objects/new.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("adds, deletes or renames");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Mets_Patch_Plus_The_Platform_Scaffold_Folders_Passes_The_Gate()
    {
        // A deposit created against an Archival Group preserved before LPII-9 gets metadata/ and
        // metadata/ad-hoc/ written into its METS, so the migration's diff for such a group is the
        // METS patch plus those two empty containers. They are how the object is recorded, not
        // what it holds - refusing them would leave every pre-LPII-9 group unmigrated.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ContainersToAdd.Add(Container("metadata"));
        job.ContainersToAdd.Add(Container("metadata/ad-hoc"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Job_That_Adds_Any_Other_Container_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ContainersToAdd.Add(Container("metadata/ad-hoc"));
        job.ContainersToAdd.Add(Container("objects/new-folder"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("adds content");
        Executed(mediator).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("https://preservation.test/repository/cc/other/metadata/ad-hoc")] // another object
    [InlineData("https://elsewhere.test/repository/cc/thing/metadata")]           // another host
    [InlineData("https://preservation.test/repository/cc/thing/data/metadata")]   // a real folder called data
    public async Task A_Container_That_Merely_Resembles_A_Scaffold_Folder_Is_Not_Tolerated(string containerId)
    {
        // The allowance is literal: the deposit's Archival Group, then exactly metadata or
        // metadata/ad-hoc, on the same host, with nothing unescaped or stripped on the way.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ContainersToAdd.Add(new DigitalPreservation.Common.Model.Container { Id = new Uri(containerId) });

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("adds content");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Scaffold_Look_Alike_With_An_Encoded_Separator_Is_Refused_As_An_Invalid_Slug_Not_As_Suppression()
    {
        // "metadata%2Fad-hoc" is one segment, not two - it would also fail the scaffold-folder
        // literal match - but issue #298's server-side slug check now runs before
        // SuppressedButNotMetsOnly, so an encoded separator is caught there first.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ContainersToAdd.Add(new DigitalPreservation.Common.Model.Container
        {
            Id = new Uri("https://preservation.test/repository/cc/thing/metadata%2Fad-hoc")
        });

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("encoded path separator");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task The_Allowance_Is_Judged_Against_The_Deposits_Archival_Group_Not_The_Jobs()
    {
        // A caller-supplied job's ArchivalGroup is just a claim. A job naming <group>/objects as
        // its Archival Group and adding <group>/objects/metadata would look like scaffold relative
        // to itself; relative to the deposit's real Archival Group it is a new folder of content.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ArchivalGroup = new Uri(ArchivalGroup + "/objects");
        job.ContainersToAdd.Add(Container("objects/metadata"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("adds content");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task The_Allowance_Is_Judged_Against_The_Deposits_Archival_Group_Not_The_Jobs_Spelling_Of_It()
    {
        // The converse: the gate takes the object from the deposit, not from whatever URI the job
        // carries. A job that names the same group differently - here on the Storage API host,
        // with a trailing slash - still gets the allowance. (This used to be shown with a job that
        // named no Archival Group at all; that is now refused as malformed before execution,
        // see issue #267, so the point is made with a differently-spelt one instead.)
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ArchivalGroup = new Uri("https://storage.test/repository/cc/thing/");
        job.ContainersToAdd.Add(Container("metadata/ad-hoc"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Patch_Of_A_Non_Mets_Binary_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = new ImportJob { SuppressActivityStreamEvent = true, Deposit = DepositUri };
        job.BinariesToPatch.Add(Binary("objects/report.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not a METS file");
        Executed(mediator).MustNotHaveHappened();
    }

    private static ImportJob MetsOnlyJob(bool suppress)
    {
        var job = new ImportJob
        {
            SuppressActivityStreamEvent = suppress,
            ArchivalGroup = ArchivalGroup,
            Deposit = DepositUri
        };
        job.BinariesToPatch.Add(Binary("mets.xml"));
        return job;
    }

}
