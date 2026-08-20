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
    public async Task Suppression_Passes_The_Gate_When_The_Migration_Flag_Is_On()
    {
        var mediator = A.Fake<IMediator>();
        var controller = Controller(mediator, migrationFlagOn: true);

        await controller.ExecuteImportJob(
            "dep-1", new ImportJob { SuppressActivityStreamEvent = true }, default);

        A.CallTo(() => mediator.Send(A<GetDeposit>._, A<CancellationToken>._)).MustHaveHappened();
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
