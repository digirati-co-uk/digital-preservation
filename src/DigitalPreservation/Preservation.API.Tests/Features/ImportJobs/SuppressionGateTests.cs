using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using FakeItEasy.Configuration;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;

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
    private const string DepositId = "dep-1";
    private static readonly Uri DepositUri = new("https://preservation.test/deposits/" + DepositId);
    private static readonly Uri DepositFiles = new("s3://deposits/" + DepositId + "/");
    private static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");

    [Fact]
    public async Task Suppression_Is_Refused_When_The_Migration_Flag_Is_Off()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: false);

        var result = await controller.ExecuteImportJob(
            DepositId, new ImportJob { SuppressActivityStreamEvent = true }, default);

        Refusal(result).Should().Contain("EnableMetsIdNormalisation");
        // Refused before anything was looked up, let alone executed.
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task An_Unsuppressed_Job_Is_Unaffected_By_The_Flag()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: false);

        await controller.ExecuteImportJob(DepositId, MetsOnlyJob(suppress: false), default);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Mets_Only_Patch_Passes_The_Gate_When_The_Flag_Is_On()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);

        await controller.ExecuteImportJob(DepositId, MetsOnlyJob(suppress: true), default);

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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

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

        await controller.ExecuteImportJob(DepositId, job, default);

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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("adds content");
        Executed(mediator).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("https://preservation.test/repository/cc/other/metadata/ad-hoc")] // another object
    [InlineData("https://elsewhere.test/repository/cc/thing/metadata")]           // another host
    [InlineData("https://preservation.test/repository/cc/thing/metadata%2Fad-hoc")] // one segment, not two
    [InlineData("https://preservation.test/repository/cc/thing/data/metadata")]   // a real folder called data
    public async Task A_Container_That_Merely_Resembles_A_Scaffold_Folder_Is_Not_Tolerated(string containerId)
    {
        // The allowance is literal: the deposit's Archival Group, then exactly metadata or
        // metadata/ad-hoc, on the same host, with nothing unescaped or stripped on the way.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ContainersToAdd.Add(new DigitalPreservation.Common.Model.Container { Id = new Uri(containerId) });

        var result = await controller.ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("adds content");
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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("adds content");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task What_The_Job_Claims_As_Its_Archival_Group_Does_Not_Matter_Only_The_Deposits()
    {
        // The converse: a job that names no Archival Group at all still gets the allowance,
        // because the deposit says which object its scaffold folders belong to.
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.ArchivalGroup = null;
        job.ContainersToAdd.Add(Container("metadata/ad-hoc"));

        await controller.ExecuteImportJob(DepositId, job, default);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Patch_Of_A_Non_Mets_Binary_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = new ImportJob { SuppressActivityStreamEvent = true, Deposit = DepositUri };
        job.BinariesToPatch.Add(Binary("objects/report.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, default);

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

    private static DigitalPreservation.Common.Model.Binary Binary(string relativePath) =>
        new()
        {
            Id = new Uri($"{ArchivalGroup}/{relativePath}"),
            Origin = new Uri(DepositFiles, relativePath)
        };

    private static DigitalPreservation.Common.Model.Container Container(string relativePath) =>
        new() { Id = new Uri($"{ArchivalGroup}/{relativePath}") };

    private static string? Refusal(IActionResult result)
    {
        var problem = result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(400);
        return problem.Detail;
    }

    private static IAssertConfiguration Executed(IMediator mediator) =>
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._));

    /// <summary>
    /// A mediator that knows one deposit, dep-1, for Archival Group cc/thing, with no import
    /// jobs yet - enough for the controller to get past its deposit checks to the gate.
    /// </summary>
    private static IMediator Mediator()
    {
        var mediator = A.Fake<IMediator>();
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull<Deposit?>(new Deposit
            {
                Id = DepositUri,
                ArchivalGroup = ArchivalGroup,
                Files = DepositFiles
            }));
        A.CallTo(() => mediator.Send(A<GetImportJobResultsForDeposit>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new List<ImportJobResult>()));
        A.CallTo(() => mediator.Send(A<ExecuteImportJob>._, A<CancellationToken>._))
            .Returns(Result.OkNotNull(new ImportJobResult
            {
                Id = new Uri(DepositUri + "/importjobs/results/job-1"),
                ImportJob = new Uri(DepositUri + "/importjobs/diff"),
                ArchivalGroup = ArchivalGroup,
                Status = ImportJobStates.Waiting
            }));
        return mediator;
    }

    private static ImportJobsController Controller(IMediator mediator, bool migrationFlagOn)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableMetsIdNormalisation"] = migrationFlagOn ? "true" : "false"
            }).Build();
        var mutator = new ResourceMutator(Options.Create(new MutatorOptions
        {
            Storage = "https://storage.test",
            Preservation = "https://preservation.test"
        }));
        return new ImportJobsController(
            NullLogger<ImportJobsController>.Instance, mediator, mutator, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
