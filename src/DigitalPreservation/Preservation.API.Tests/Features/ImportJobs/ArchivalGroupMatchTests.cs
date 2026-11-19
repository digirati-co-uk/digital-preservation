using DigitalPreservation.Common.Model.Import;
using FakeItEasy;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.ImportJobs.Requests;
using static Preservation.API.Tests.Features.ImportJobs.ImportJobsControllerHarness;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// An Import Job's archivalGroup is meant to be redundant with the deposit's, but a hand-written
/// job could name any group in the repository and the platform would make a new version of it,
/// with content from this deposit's workspace, and report success. These pin the check that
/// stops that (issue #267): the two must name the same repository path, compared by path so that a
/// host difference is not a mismatch, and compared as escaped because that is what Storage resolves.
/// </summary>
public class ArchivalGroupMatchTests
{
    [Fact]
    public async Task A_Job_Naming_A_Different_Archival_Group_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = Job(new Uri("https://preservation.test/repository/cc/someone-elses-thing"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("does not match the Deposit's Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_No_Archival_Group_Is_Refused()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = Job(archivalGroup: null);

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("must declare which Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_Naming_The_Deposits_Archival_Group_Is_Executed()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);

        await controller.ExecuteImportJob(DepositId, Job(ArchivalGroup), TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task The_Same_Archival_Group_On_The_Storage_Host_Is_Accepted()
    {
        // The comparison is by repository path: a job that names the group on the Storage API
        // host, or with a trailing slash, is the same group and must not be refused for it.
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = Job(new Uri("https://storage.test/repository/cc/thing/"));

        await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Executed(mediator).MustHaveHappened();
    }

    [Fact]
    public async Task A_Job_Whose_Group_Only_Matches_Once_Percent_Decoded_Is_Refused()
    {
        // '%' is a legal slug character, so a group whose one segment is foo%2Fbar is a different
        // object from the two-segment foo/bar. Storage resolves the job's path in Fedora exactly as
        // posted, so the comparison must not decode first - if it did, a job for the two-segment
        // object would be accepted against a deposit for the one-segment one.
        var mediator = Mediator(new Uri("https://preservation.test/repository/cc/foo%2Fbar"));
        var controller = Controller(mediator);
        var job = Job(new Uri("https://preservation.test/repository/cc/foo/bar"));

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("does not match the Deposit's Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_A_Relative_Archival_Group_Is_Refused_Rather_Than_Crashing()
    {
        // A bare string in the JSON deserialises as a relative Uri, which the path comparison
        // cannot address (AbsolutePath throws) - it must land as this 400, never a 500.
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = new ImportJob { Deposit = DepositUri, ArchivalGroup = new Uri("invalid-group", UriKind.Relative) };
        job.BinariesToAdd.Add(new DigitalPreservation.Common.Model.Binary
        {
            Id = new Uri($"{ArchivalGroup}/objects/page-001.tif"),
            Origin = new Uri(DepositFiles, "objects/page-001.tif")
        });

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("does not match the Deposit's Archival Group");
        Executed(mediator).MustNotHaveHappened();
    }

    [Fact]
    public async Task A_Job_With_A_Relative_Deposit_Is_Refused_Rather_Than_Crashing()
    {
        var mediator = Mediator();
        var controller = Controller(mediator);
        var job = Job(ArchivalGroup);
        job.Deposit = new Uri(DepositId, UriKind.Relative);

        var result = await controller.ExecuteImportJob(DepositId, job, TestContext.Current.CancellationToken);

        Refusal(result).Should().Contain("does not match the Deposit it was submitted to");
        Executed(mediator).MustNotHaveHappened();
    }

    private static ImportJob Job(Uri? archivalGroup)
    {
        var job = new ImportJob { Deposit = DepositUri, ArchivalGroup = archivalGroup };
        job.BinariesToAdd.Add(new DigitalPreservation.Common.Model.Binary
        {
            Id = new Uri($"{archivalGroup ?? ArchivalGroup}/objects/page-001.tif"),
            Origin = new Uri(DepositFiles, "objects/page-001.tif")
        });
        return job;
    }
}
