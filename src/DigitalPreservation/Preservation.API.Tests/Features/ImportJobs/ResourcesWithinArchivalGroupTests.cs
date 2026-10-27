using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using MediatR;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;
using Binary = DigitalPreservation.Common.Model.Binary;
using Container = DigitalPreservation.Common.Model.Container;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// #271 made a posted Import Job name the same Archival Group as its Deposit. That left the job's
/// own contents unchecked: each binary and container id could still point anywhere in the
/// repository, and the Storage API would write there. These pin that every id must lie strictly
/// below the job's Archival Group.
/// </summary>
public class ResourcesWithinArchivalGroupTests
{
    [Fact]
    public async Task A_Job_Whose_Resources_Are_All_Below_Its_Group_Is_Executed()
    {
        var mediator = Mediator();
        var job = Job();
        job.ContainersToAdd.Add(new Container { Id = new Uri(ArchivalGroup + "/objects/sub") });
        job.BinariesToDelete.Add(new Binary { Id = new Uri(ArchivalGroup + "/objects/old.tif") });

        await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Binary_In_Another_Archival_Group_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToDelete.Add(new Binary { Id = new Uri("https://preservation.test/repository/cc/someone-elses-thing/objects/x.tif") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Container_In_Another_Archival_Group_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.ContainersToAdd.Add(new Container { Id = new Uri("https://preservation.test/repository/cc/thing-2/objects") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

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

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Sibling_Group_Sharing_A_Name_Prefix_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToDelete.Add(new Binary { Id = new Uri("https://preservation.test/repository/cc/thing2/objects/x.tif") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not within its Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Resource_With_No_Id_Is_Refused()
    {
        var mediator = Mediator();
        var job = Job();
        job.ContainersToDelete.Add(new Container());

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

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

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

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

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("not a child of deposit file location");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Binary_With_No_Origin_Is_Refused_Rather_Than_Crashing()
    {
        var mediator = Mediator();
        var job = Job();
        job.BinariesToPatch.Add(new Binary { Id = new Uri(ArchivalGroup + "/objects/page-002.tif") });

        var result = await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

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

        await Controller(mediator).ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

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
}
