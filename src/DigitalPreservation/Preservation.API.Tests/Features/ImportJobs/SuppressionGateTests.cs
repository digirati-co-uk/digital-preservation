using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs;
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
    [Fact]
    public async Task Suppression_Is_Refused_When_The_Migration_Flag_Is_Off()
    {
        var mediator = A.Fake<IMediator>();
        var controller = Controller(mediator, migrationFlagOn: false);

        var result = await controller.ExecuteImportJob(
            "dep-1", new ImportJob { SuppressActivityStreamEvent = true }, default);

        var problem = result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(400);
        problem.Detail.Should().Contain("EnableMetsIdNormalisation");
        // Refused before anything was looked up, let alone executed.
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task An_Unsuppressed_Job_Is_Unaffected_By_The_Flag()
    {
        var mediator = A.Fake<IMediator>();
        var controller = Controller(mediator, migrationFlagOn: false);

        await controller.ExecuteImportJob("dep-1", new ImportJob(), default);

        // Past the gate: the ordinary pipeline (starting with the deposit fetch) took over.
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Mets_Only_Patch_Passes_The_Gate_When_The_Flag_Is_On()
    {
        var mediator = A.Fake<IMediator>();
        var controller = Controller(mediator, migrationFlagOn: true);

        await controller.ExecuteImportJob("dep-1", MetsOnlyJob(suppress: true), default);

        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Job_That_Changes_Content_Is_Refused_Even_With_The_Flag_On()
    {
        // The flag says WHEN suppression may be used; this says WHAT FOR. A suppressed content
        // change would preserve a real new version that IIIF is never told to rebuild from - and
        // until now the only thing preventing it was the migration tool's client-side check.
        var mediator = A.Fake<IMediator>();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = MetsOnlyJob(suppress: true);
        job.BinariesToAdd.Add(new DigitalPreservation.Common.Model.Binary
        {
            Id = new Uri("https://preservation.test/repository/cc/thing/objects/new.pdf")
        });

        var result = await controller.ExecuteImportJob("dep-1", job, default);

        var problem = result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(400);
        problem.Detail.Should().Contain("adds, deletes or renames");
        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Suppressed_Patch_Of_A_Non_Mets_Binary_Is_Refused()
    {
        var mediator = A.Fake<IMediator>();
        var controller = Controller(mediator, migrationFlagOn: true);
        var job = new ImportJob { SuppressActivityStreamEvent = true };
        job.BinariesToPatch.Add(new DigitalPreservation.Common.Model.Binary
        {
            Id = new Uri("https://preservation.test/repository/cc/thing/objects/report.pdf")
        });

        var result = await controller.ExecuteImportJob("dep-1", job, default);

        result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Detail.Should().Contain("not a METS file");
    }

    private static ImportJob MetsOnlyJob(bool suppress)
    {
        var job = new ImportJob { SuppressActivityStreamEvent = suppress };
        job.BinariesToPatch.Add(new DigitalPreservation.Common.Model.Binary
        {
            Id = new Uri("https://preservation.test/repository/cc/thing/mets.xml")
        });
        return job;
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
