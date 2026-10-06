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
/// A job whose ContainersToAdd or BinariesToAdd contains an invalid slug is refused with 400
/// before anything is queued or persisted (issue #298). Previously this was only checked
/// client-side, by the diff page; a caller posting a job directly was sent to Fedora unchecked.
/// </summary>
public class InvalidSlugRefusalTests
{
    private const string DepositId = "dep-1";
    private static readonly Uri DepositUri = new("https://preservation.test/deposits/" + DepositId);
    private static readonly Uri DepositFiles = new("s3://deposits/" + DepositId + "/");
    private static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");

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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

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

        await controller.ExecuteImportJob(DepositId, job, default);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Job_That_Only_Patches_An_Item_With_An_Invalid_Slug_Is_Not_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToPatch.Add(Binary("objects/report@final.txt"));

        await controller.ExecuteImportJob(DepositId, job, default);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Job_That_Only_Deletes_An_Item_With_An_Invalid_Slug_Is_Not_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToDelete.Add(Binary("objects/report@final.txt"));

        await controller.ExecuteImportJob(DepositId, job, default);

        Executed(mediator).MustHaveHappened();
    }

    private static ImportJob PlainJob() => new() { ArchivalGroup = ArchivalGroup, Deposit = DepositUri };

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

    private static ImportJobsController Controller(IMediator mediator)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableMetsIdNormalisation"] = "true"
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
