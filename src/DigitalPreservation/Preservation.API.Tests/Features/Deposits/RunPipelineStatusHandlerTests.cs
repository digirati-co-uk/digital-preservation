using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Preservation.API.Data.Entities;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Tests.TestingInfrastructure;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// Starting a pipeline job is a claim on it, not a status report (issue #221). The pipeline is driven
/// by SNS/SQS, which is at-least-once, so the same start message can arrive more than once for a
/// single job; only the delivery that moves the job out of "waiting" may run it. Running it twice
/// re-scans the deposit and appends a second virus-scan provenance event for a scan that only
/// happened once.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class RunPipelineStatusHandlerTests(DatabaseFixture fixture)
{
    private static readonly ClaimsPrincipal Tester =
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

    [Fact]
    public async Task Claiming_A_Waiting_Job_Succeeds_And_Starts_It()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Waiting);

        var result = await Handle(context, depositId, jobId, PipelineJobStates.Running);

        result.Success.Should().BeTrue();
        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.Running);
        job.DateBegun.Should().NotBeNull("starting the job records when it began");
    }

    [Theory]
    [InlineData(PipelineJobStates.Running)]
    [InlineData(PipelineJobStates.MetadataCreated)]
    [InlineData(PipelineJobStates.Completed)]
    [InlineData(PipelineJobStates.CompletedWithErrors)]
    public async Task Claiming_A_Job_That_Is_Not_Waiting_Is_Refused(string alreadyIn)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, alreadyIn);

        var result = await Handle(context, depositId, jobId, PipelineJobStates.Running);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict,
            "the caller has to be able to tell a repeat delivery from a transient failure");

        var job = await ReloadJob(jobId);
        job.Status.Should().Be(alreadyIn, "a refused claim must not disturb the job it lost to");
        job.DateBegun.Should().BeNull();
    }

    [Fact]
    public async Task Only_One_Of_Two_Concurrent_Claims_Wins()
    {
        await using var seedContext = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(seedContext, PipelineJobStates.Waiting);

        // Separate contexts, so this is two consumers racing rather than one change tracker used
        // twice. Read-then-write would let both see "waiting" and both proceed.
        await using var first = fixture.CreateNewAuthServiceContext();
        await using var second = fixture.CreateNewAuthServiceContext();

        var results = await Task.WhenAll(
            Handle(first, depositId, jobId, PipelineJobStates.Running),
            Handle(second, depositId, jobId, PipelineJobStates.Running));

        results.Count(r => r.Success).Should().Be(1, "exactly one delivery may run the job");
        results.Single(r => !r.Success).ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Completing_A_Job_Still_Records_The_Finish_And_Any_Errors()
    {
        // The claim path returns early, so this guards the transitions either side of it.
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running);

        var result = await Handle(context, depositId, jobId, PipelineJobStates.CompletedWithErrors, "it broke");

        result.Success.Should().BeTrue();
        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.CompletedWithErrors);
        job.DateFinished.Should().NotBeNull();
        job.Errors.Should().Be("it broke");
    }

    private static Task<DigitalPreservation.Common.Model.Results.Result> Handle(
        Preservation.API.Data.PreservationContext context,
        string depositId, string jobId, string status, string? errors = null)
    {
        var handler = new RunPipelineStatusHandler(new NullLogger<RunPipelineStatusHandler>(), context);
        var pipelineDeposit = new PipelineDeposit
        {
            Id = jobId,
            DepositId = depositId,
            Status = status,
            RunUser = "tester",
            Errors = errors
        };
        return handler.Handle(new RunPipelineStatus(pipelineDeposit, Tester), CancellationToken.None);
    }

    private static async Task<(string DepositId, string JobId)> SeedJob(
        Preservation.API.Data.PreservationContext context, string status)
    {
        var depositId = $"dep-{Guid.NewGuid()}";
        var jobId = $"job-{Guid.NewGuid()}";

        context.Deposits.Add(new DepositEntity
        {
            MintedId = depositId,
            Status = DepositStates.New,
            Active = true,
            Created = DateTime.UtcNow,
            CreatedBy = "tester",
            LastModified = DateTime.UtcNow,
            LastModifiedBy = "tester"
        });
        context.PipelineRunJobs.Add(new PipelineRunJob
        {
            Id = jobId,
            Deposit = depositId,
            ArchivalGroup = null,
            Status = status,
            DateSubmitted = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            PipelineJobJson = "{}",
            RunUser = "tester"
        });
        await context.SaveChangesAsync();

        return (depositId, jobId);
    }

    private async Task<PipelineRunJob> ReloadJob(string jobId)
    {
        // A fresh context, because the claim is executed as a conditional UPDATE in the database and
        // is deliberately invisible to any change tracker that loaded the row beforehand.
        await using var context = fixture.CreateNewAuthServiceContext();
        return await context.PipelineRunJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
    }
}
