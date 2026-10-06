using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using FakeItEasy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Preservation.API.Features.Deposits.PipelineCleanup;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Tests.TestingInfrastructure;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits.PipelineCleanup;

/// <summary>
/// The Deposit page's GET used to tidy up stalled pipeline runs as a side effect of rendering - only
/// when someone happened to open that deposit, twice over if two people opened it at once, and it
/// released whatever lock was on the deposit regardless of who held it. This moves that cleanup into
/// a Preservation API background sweep (issue #301) that runs on a timer regardless of page views,
/// and - per the adversarial review of #309 - only releases the lock when the job it is closing is
/// the deposit's most recent, since a user who started a *new* run keeps the same lock (#299's
/// "transfer" decision) and closing an old stuck job must not take it from them.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class PipelineJobCleanupProcessorTests(DatabaseFixture fixture)
{
    private const double CleanupMinutes = 60;
    private static readonly DateTime Overdue = DateTime.UtcNow.AddMinutes(-(CleanupMinutes + 30));
    private static readonly DateTime NotOverdue = DateTime.UtcNow.AddMinutes(-5);

    [Fact]
    public async Task An_Overdue_Incomplete_Job_Is_Closed_And_Its_Lock_Released_When_The_Runs_User_Held_It()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running, dateBegun: Overdue,
            runUser: "tester", depositLockedBy: "tester");

        await RunSweep(context);

        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.CompletedWithErrors);
        job.Errors.Should().Be("Cleaned up as previous processing did not complete");
        job.DateFinished.Should().NotBeNull();
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().BeNull("the run that held the lock has just been closed out");
    }

    [Fact]
    public async Task A_Lock_Held_By_Someone_Else_Is_Left_Alone()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running, dateBegun: Overdue,
            runUser: "tester", depositLockedBy: "someone-else");

        await RunSweep(context);

        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.CompletedWithErrors);
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().Be("someone-else",
            "someone else may have force-taken the lock mid-run; closing a run that isn't theirs must not take it");
    }

    [Fact]
    public async Task A_Completed_Job_Is_Untouched_Even_Though_It_Is_Old()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Completed, dateBegun: Overdue,
            runUser: "tester", depositLockedBy: null);
        var before = await ReloadJob(jobId);

        await RunSweep(context);

        var after = await ReloadJob(jobId);
        after.Status.Should().Be(PipelineJobStates.Completed);
        after.DateFinished.Should().Be(before.DateFinished);
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().BeNull();
    }

    [Fact]
    public async Task Running_The_Sweep_Twice_Is_Idempotent()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running, dateBegun: Overdue,
            runUser: "tester", depositLockedBy: "tester");

        await RunSweep(context);
        var afterFirst = await ReloadJob(jobId);

        // A new run takes the lock between the two sweeps - the second sweep must not strip it from
        // this new run on behalf of the job it already closed out.
        await using (var relockContext = fixture.CreateNewAuthServiceContext())
        {
            var deposit = await relockContext.Deposits.SingleAsync(d => d.MintedId == depositId);
            deposit.LockedBy = "tester";
            deposit.LockDate = DateTime.UtcNow;
            await relockContext.SaveChangesAsync();
        }

        await RunSweep(context);
        var afterSecond = await ReloadJob(jobId);

        afterSecond.Status.Should().Be(afterFirst.Status);
        afterSecond.DateFinished.Should().Be(afterFirst.DateFinished);
        afterSecond.Errors.Should().Be(afterFirst.Errors);
        var finalDeposit = await ReloadDeposit(depositId);
        finalDeposit.LockedBy.Should().Be("tester",
            "the job was already closed out by the first sweep, so the second must not touch the new run's lock");
    }

    [Fact]
    public async Task An_Overdue_Job_That_Is_Not_The_Deposits_Latest_Is_Closed_Without_Releasing_The_Lock()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var depositId = $"dep-{Guid.NewGuid()}";
        await SeedDeposit(context, depositId, lockedBy: "tester");
        var oldJobId = await SeedPipelineRunJob(context, depositId, PipelineJobStates.Running,
            dateSubmitted: Overdue, dateBegun: Overdue, runUser: "tester");
        // The newer run that now holds the lock - started after the old one stalled.
        await SeedPipelineRunJob(context, depositId, PipelineJobStates.Running,
            dateSubmitted: NotOverdue, dateBegun: NotOverdue, runUser: "tester");

        await RunSweep(context);

        var oldJob = await ReloadJob(oldJobId);
        oldJob.Status.Should().Be(PipelineJobStates.CompletedWithErrors,
            "the stale job is still closed out so it stops showing as running");
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().Be("tester",
            "the lock belongs to the newer run now (issue #299's transfer decision); closing the old job must not take it");
    }

    private static async Task RunSweep(PreservationContext context)
    {
        var mediator = MediatorForwardingToRealHandler(context);
        var options = Options.Create(new PipelineOptions
        {
            PipelineJobTopicArn = "arn:aws:sns:eu-west-1:000000000000:test-topic",
            PipelineJobQueue = "test-queue",
            PipelineJobsCleanupMinutes = CleanupMinutes
        });
        var processor = new PipelineJobCleanupProcessor(
            context, options, mediator, NullLogger<PipelineJobCleanupProcessor>.Instance);

        var result = await processor.CleanupOverdueJobs(CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    /// <summary>
    /// Forwards to a real RunPipelineStatusHandler against the same context, rather than mocking the
    /// outcome - the point of these tests is that the sweep's "latest job" release goes through that
    /// handler's own conditional release (issue #299), not a second copy of it.
    /// </summary>
    private static IMediator MediatorForwardingToRealHandler(PreservationContext context)
    {
        var handler = new RunPipelineStatusHandler(NullLogger<RunPipelineStatusHandler>.Instance, context);
        var mediator = A.Fake<IMediator>();
        A.CallTo(() => mediator.Send(A<RunPipelineStatus>._, A<CancellationToken>._))
            .ReturnsLazily(call => handler.Handle((RunPipelineStatus)call.Arguments[0]!, (CancellationToken)call.Arguments[1]!));
        return mediator;
    }

    private static async Task<(string DepositId, string JobId)> SeedJob(
        PreservationContext context, string status, DateTime dateBegun,
        string runUser, string? depositLockedBy)
    {
        var depositId = $"dep-{Guid.NewGuid()}";
        await SeedDeposit(context, depositId, depositLockedBy);
        var jobId = await SeedPipelineRunJob(context, depositId, status, dateBegun, dateBegun, runUser);
        return (depositId, jobId);
    }

    private static async Task SeedDeposit(PreservationContext context, string depositId, string? lockedBy)
    {
        context.Deposits.Add(new DepositEntity
        {
            MintedId = depositId,
            Status = DepositStates.New,
            Active = true,
            Created = DateTime.UtcNow,
            CreatedBy = "tester",
            LastModified = DateTime.UtcNow,
            LastModifiedBy = "tester",
            LockedBy = lockedBy,
            LockDate = lockedBy != null ? DateTime.UtcNow : null
        });
        await context.SaveChangesAsync();
    }

    private static async Task<string> SeedPipelineRunJob(
        PreservationContext context, string depositId, string status,
        DateTime dateSubmitted, DateTime? dateBegun, string runUser)
    {
        var jobId = $"job-{Guid.NewGuid()}";
        context.PipelineRunJobs.Add(new PipelineRunJob
        {
            Id = jobId,
            Deposit = depositId,
            ArchivalGroup = null,
            Status = status,
            DateSubmitted = dateSubmitted,
            DateBegun = dateBegun,
            LastUpdated = DateTime.UtcNow,
            PipelineJobJson = "{}",
            RunUser = runUser
        });
        await context.SaveChangesAsync();
        return jobId;
    }

    private async Task<PipelineRunJob> ReloadJob(string jobId)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        return await context.PipelineRunJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
    }

    private async Task<DepositEntity> ReloadDeposit(string depositId)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        return await context.Deposits.AsNoTracking().SingleAsync(d => d.MintedId == depositId);
    }
}
