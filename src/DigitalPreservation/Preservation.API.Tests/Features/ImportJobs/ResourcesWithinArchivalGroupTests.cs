using DigitalPreservation.Common.Model;
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
/// #271 made a posted Import Job name the same Archival Group as its Deposit. That left the job's
/// own contents unchecked: each binary and container id could still point anywhere in the
/// repository, and the Storage API would write there. These pin that every id must lie strictly
/// below the job's Archival Group.
/// </summary>
public class ResourcesWithinArchivalGroupTests
{
    private const string DepositId = "dep-1";
    private static readonly Uri DepositUri = new("https://preservation.test/deposits/" + DepositId);
    private static readonly Uri DepositFiles = new("s3://deposits/" + DepositId + "/");
    private static readonly Uri ArchivalGroup = new("https://preservation.test/repository/cc/thing");

    [Fact]
    public async Task A_Job_Whose_Resources_Are_All_Below_Its_Group_Is_Executed()
    {
        var mediator = Mediator();
        var job = Job();
        job.ContainersToAdd.Add(new Container { Id = new Uri(ArchivalGroup + "/objects/sub") });
        job.BinariesToDelete.Add(new Binary { Id = new Uri(ArchivalGroup + "/objects/old.tif") });

        await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Binary_In_Another_Archival_Group_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToDelete.Add(new Binary { Id = new Uri("https://preservation.test/repository/cc/someone-elses-thing/objects/x.tif") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Container_In_Another_Archival_Group_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.ContainersToAdd.Add(new Container { Id = new Uri("https://preservation.test/repository/cc/thing-2/objects") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task The_Archival_Group_Itself_Is_Not_A_Valid_Resource_Id()
    {
        // What a file uploaded under the name ".." used to produce: a binary whose id is the group.
        var mediator = Mediator();
        var job = Job();
        job.BinariesToAdd.Add(new Binary { Id = ArchivalGroup, Origin = new Uri(DepositFiles, "objects/..") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Sibling_Group_Sharing_A_Name_Prefix_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToDelete.Add(new Binary { Id = new Uri("https://preservation.test/repository/cc/thing2/objects/x.tif") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Resource_With_No_Id_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.ContainersToDelete.Add(new Container());

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("must have an id");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Relative_Resource_Id_Is_Refused_Rather_Than_Crashing()
    {
        // The e2e regression (2026-09-17): a bare string in the JSON ("invalid-id") deserialises
        // as a RELATIVE Uri, and the containment check's path helpers throw on those - the job
        // must be refused with this 400, never a 500.
        var mediator = Mediator();
        var job = Job();
        job.ContainersToAdd.Add(new Container { Id = new Uri("invalid-id", UriKind.Relative) });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("absolute URI");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Relative_Binary_Origin_Is_Refused_Rather_Than_Crashing()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToAdd.Add(new Binary
        {
            Id = new Uri(ArchivalGroup + "/objects/page-002.tif"),
            Origin = new Uri("objects/page-002.tif", UriKind.Relative)
        });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("not a child of deposit file location");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Binary_With_No_Origin_Is_Refused_Rather_Than_Crashing()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToPatch.Add(new Binary { Id = new Uri(ArchivalGroup + "/objects/page-002.tif") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Refusal(result).Should().Contain("not a child of deposit file location");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task The_Same_Group_On_Another_Host_Is_Accepted_Because_Only_The_Path_Is_The_Identity()
    {
        // Deliberate, and worth pinning: the check is by repository path, as #271's is, because ids
        // may carry the Storage API host or the Preservation API host for the same resource. The
        // Storage API resolves an id by its path alone, so a different host changes nothing about
        // where the resource is written.
        var mediator = Mediator();
        var job = Job();
        job.BinariesToAdd.Add(new Binary
        {
            Id = new Uri("https://storage.test/repository/cc/thing/objects/page-002.tif"),
            Origin = new Uri(DepositFiles, "objects/page-002.tif")
        });

        await Controller(mediator).ExecuteImportJob(DepositId, job, default);

        Executed(mediator).MustHaveHappened();
    }

    private static ImportJob Job()
    {
        var job = new ImportJob { Deposit = DepositUri, ArchivalGroup = ArchivalGroup };
        job.BinariesToAdd.Add(new Binary
        {
            Id = new Uri(ArchivalGroup + "/objects/page-001.tif"),
            Origin = new Uri(DepositFiles, "objects/page-001.tif")
        });
        return job;
    }

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
            .Returns(Result.OkNotNull<Deposit?>(new Deposit { Id = DepositUri, ArchivalGroup = ArchivalGroup, Files = DepositFiles }));
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
            new Dictionary<string, string?> { ["FeatureFlags:EnableMetsIdNormalisation"] = "false" }).Build();
        var mutator = new ResourceMutator(Options.Create(new MutatorOptions
        {
            Storage = "https://storage.test",
            Preservation = "https://preservation.test"
        }));
        return new ImportJobsController(NullLogger<ImportJobsController>.Instance, mediator, mutator, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
