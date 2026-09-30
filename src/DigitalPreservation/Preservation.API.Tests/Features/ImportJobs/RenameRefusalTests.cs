using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using FakeItEasy.Configuration;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// Renaming (changing only a Container or Binary's display name, never its slug or path) is not
/// implemented yet, so a job that asks for one is refused with 400 before anything is queued or
/// persisted (issue #260).
/// </summary>
public class RenameRefusalTests
{
    private const string DepositId = "dep-1";
    private static readonly Uri DepositUri = new("https://preservation.test/deposits/" + DepositId);
    private static readonly Uri DepositFiles = new("s3://deposits/" + DepositId + "/");
    private static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");

    [Fact]
    public async Task A_Job_With_A_Binary_Rename_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = PlainJob();
        job.BinariesToRename.Add(Binary("objects/report.pdf"));

        var result = await controller.ExecuteImportJob(DepositId, job, default);

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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

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

        var result = await controller.ExecuteImportJob(DepositId, diffReference, default);

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

        await controller.ExecuteImportJob(DepositId, job, default);

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

        var result = await controller.ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("Renaming is not supported yet");
        Executed(mediator).MustNotHaveHappened();
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
        var urlHelper = A.Fake<IUrlHelper>();
        A.CallTo(() => urlHelper.RouteUrl(A<UrlRouteContext>._))
            .Returns(DepositUri + "/importjobs/diff");
        return new ImportJobsController(
            NullLogger<ImportJobsController>.Instance, mediator, mutator, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = urlHelper
        };
    }
}
