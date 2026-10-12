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

    [Theory]
    [InlineData(PipelineJobStates.Completed)]
    [InlineData(PipelineJobStates.CompletedWithErrors)]
    public async Task A_Terminal_Status_From_Running_Releases_A_Lock_Held_By_The_Runs_User(string terminalStatus)
    {
        // This is the one place every way a run ends releases the lock: success, failure, force
        // complete from the UI, and the UI's stale-job tidy-up all post a terminal status here.
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running, runUser: "tester",
            depositLockedBy: "tester");

        var result = await Handle(context, depositId, jobId, terminalStatus, terminalStatus == PipelineJobStates.CompletedWithErrors ? "it broke" : null);

        result.Success.Should().BeTrue();
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().BeNull("the run that held the lock has just ended");
        deposit.LockDate.Should().BeNull();
    }

    [Fact]
    public async Task A_Repeated_Terminal_Report_Does_Not_Release_A_Lock_Someone_Else_Has_Since_Taken()
    {
        // A run can report a terminal status twice: force complete posts CompletedWithErrors, then
        // the running job reports again when it notices. Between those two reports the same user may
        // already have started a new run and holds the lock again - the second report must not strip it.
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.CompletedWithErrors, runUser: "tester",
            depositLockedBy: "tester");

        var result = await Handle(context, depositId, jobId, PipelineJobStates.CompletedWithErrors, "it broke again");

        result.Success.Should().BeTrue();
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().Be("tester", "the job was already terminal, so this report must not touch the lock");
    }

    [Fact]
    public async Task A_Terminal_Status_Does_Not_Release_A_Lock_Held_By_A_Different_Identity()
    {
        // Someone else may have force-taken the lock mid-run (POST /lock?force=true). Releasing
        // theirs on behalf of a run that is not theirs would be wrong.
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running, runUser: "tester",
            depositLockedBy: "someone-else");

        var result = await Handle(context, depositId, jobId, PipelineJobStates.Completed);

        result.Success.Should().BeTrue();
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().Be("someone-else");
    }

    /// <summary>
    /// Before issue #316, the lock release was conditional on wasAlreadyTerminal, but the status
    /// write itself was not: a late report with a *different* terminal status than the one already
    /// recorded would still overwrite it. Force-completing a job (completedWithErrors) and then
    /// having the running job itself report (completed) when it eventually notices must not flip the
    /// status back.
    /// </summary>
    [Fact]
    public async Task A_Late_Completed_Report_Does_Not_Overwrite_An_Already_CompletedWithErrors_Status()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.CompletedWithErrors,
            runUser: "tester", depositLockedBy: "tester");

        var result = await Handle(context, depositId, jobId, PipelineJobStates.Completed);

        result.Success.Should().BeTrue("a late report that changes nothing is still a success, not an error");
        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.CompletedWithErrors,
            "a late Completed report must not overwrite a job that was already force-completed with errors");
    }

    /// <summary>
    /// The release decision (issue #316) comes from a WHERE clause the database evaluates at update
    /// time, not from a value read earlier in the handler - so a lock force-taken by someone else
    /// after this job started, but before the release runs, survives. Deliberately starts locked by
    /// the run's own user (matching RunUser), then changes it via a second context, so this is a
    /// genuinely different scenario from "locked by someone else from the start".
    /// </summary>
    [Fact]
    public async Task A_Lock_Force_Taken_By_Someone_Else_Before_The_Release_Runs_Survives_It()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running,
            runUser: "tester", depositLockedBy: "tester");

        await using (var forceContext = fixture.CreateNewAuthServiceContext())
        {
            var deposit = await forceContext.Deposits.SingleAsync(d => d.MintedId == depositId);
            deposit.LockedBy = "someone-else";
            deposit.LockDate = DateTime.UtcNow;
            await forceContext.SaveChangesAsync();
        }

        var result = await Handle(context, depositId, jobId, PipelineJobStates.CompletedWithErrors, "it broke");

        result.Success.Should().BeTrue();
        var deposit2 = await ReloadDeposit(depositId);
        deposit2.LockedBy.Should().Be("someone-else",
            "the release checks who holds the lock right now, not who held it when this job started");
    }

    /// <summary>
    /// Two terminal reports for the same job (e.g. force complete from the UI, and the running job
    /// itself reporting when it eventually notices) is only harmful if the same user has retaken the
    /// lock in between (issue #316's framing exactly): the first report lands and releases the lock;
    /// a new run by the same user takes it again; the second, stale report must not re-release it or
    /// overwrite the first report's status. The second report's handler invocation already read the
    /// job as "not yet terminal" before the first report landed - simulated deterministically via EF's
    /// identity resolution (a tracking query never overwrites an already-tracked entity's in-memory
    /// values) rather than by racing real concurrent timing, which reproduces the same bug the old
    /// wasAlreadyTerminal-read-then-write approach had without being flaky.
    /// </summary>
    [Fact]
    public async Task A_Stale_Second_Report_Does_Not_Release_A_Lock_The_Same_User_Has_Retaken_For_A_New_Run()
    {
        await using var lateContext = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(lateContext, PipelineJobStates.Running,
            runUser: "tester", depositLockedBy: "tester");

        // The late report's own handler invocation already has this job tracked as "processing" -
        // stale the moment the first report (below) moves it.
        await lateContext.PipelineRunJobs.SingleAsync(j => j.Id == jobId);

        await using (var firstContext = fixture.CreateNewAuthServiceContext())
        {
            var firstResult = await Handle(firstContext, depositId, jobId, PipelineJobStates.CompletedWithErrors, "it broke");
            firstResult.Success.Should().BeTrue();
        }

        // The same user starts a new run and takes the lock again before the stale report arrives.
        await using (var relockContext = fixture.CreateNewAuthServiceContext())
        {
            var deposit = await relockContext.Deposits.SingleAsync(d => d.MintedId == depositId);
            deposit.LockedBy = "tester";
            deposit.LockDate = DateTime.UtcNow;
            await relockContext.SaveChangesAsync();
        }

        var lateResult = await Handle(lateContext, depositId, jobId, PipelineJobStates.Completed);

        lateResult.Success.Should().BeTrue("a stale report that changes nothing is still a success, not an error");
        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.CompletedWithErrors,
            "the first report's move already landed; the stale second report must not overwrite it");
        var deposit2 = await ReloadDeposit(depositId);
        deposit2.LockedBy.Should().Be("tester",
            "the lock belongs to the new run now, not the stale report that already lost the race");
    }

    [Fact]
    public async Task Claiming_A_Job_As_Running_Does_Not_Touch_The_Lock()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Waiting, runUser: "tester",
            depositLockedBy: "tester");

        var result = await Handle(context, depositId, jobId, PipelineJobStates.Running);

        result.Success.Should().BeTrue();
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().Be("tester");
    }

    [Fact]
    public async Task Reporting_MetadataCreated_Does_Not_Touch_The_Lock()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running, runUser: "tester",
            depositLockedBy: "tester");

        var result = await Handle(context, depositId, jobId, PipelineJobStates.MetadataCreated);

        result.Success.Should().BeTrue();
        var deposit = await ReloadDeposit(depositId);
        deposit.LockedBy.Should().Be("tester");
        var job = await ReloadJob(jobId);
        job.Status.Should().Be(PipelineJobStates.MetadataCreated);
    }

    /// <summary>
    /// The pipeline posts MetadataCreated after its last force-complete check, so the report can
    /// land on a job that was force-completed (or closed by the stalled-run sweep) a moment earlier.
    /// It must not move that job back to non-terminal: if it did, the run's own later Completed
    /// report would move it again, overwriting the status and releasing the lock the same user has
    /// since retaken for a new run (issue #316).
    /// </summary>
    [Fact]
    public async Task A_Late_MetadataCreated_Report_Does_Not_Reopen_A_Finished_Job()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.CompletedWithErrors,
            runUser: "tester", depositLockedBy: null);

        var lateIntermediate = await Handle(context, depositId, jobId, PipelineJobStates.MetadataCreated);

        lateIntermediate.Success.Should().BeTrue("a late report that changes nothing is still a success, not an error");
        (await ReloadJob(jobId)).Status.Should().Be(PipelineJobStates.CompletedWithErrors,
            "a late MetadataCreated must not move a finished job back to non-terminal");

        // The same user starts a new run and takes the lock again, then the old run's own
        // Completed report arrives.
        await using (var relockContext = fixture.CreateNewAuthServiceContext())
        {
            var deposit = await relockContext.Deposits.SingleAsync(d => d.MintedId == depositId);
            deposit.LockedBy = "tester";
            deposit.LockDate = DateTime.UtcNow;
            await relockContext.SaveChangesAsync();
        }

        await using var lateContext = fixture.CreateNewAuthServiceContext();
        var lateCompleted = await Handle(lateContext, depositId, jobId, PipelineJobStates.Completed);

        lateCompleted.Success.Should().BeTrue();
        (await ReloadJob(jobId)).Status.Should().Be(PipelineJobStates.CompletedWithErrors,
            "the job was never reopened, so the late Completed has nothing to move");
        (await ReloadDeposit(depositId)).LockedBy.Should().Be("tester",
            "the lock belongs to the new run, and the old run's late report must not release it");
    }

    /// <summary>
    /// Job states only move forward (issue #356). A "waiting" report on a running job used to move it
    /// back to waiting, which ClaimJob's WHERE status = 'waiting' then let a duplicate SQS delivery of
    /// its start message claim again - running Brunnhilde twice over one deposit.
    /// </summary>
    [Theory]
    [InlineData(PipelineJobStates.Running)]
    [InlineData(PipelineJobStates.MetadataCreated)]
    public async Task A_Waiting_Report_Is_Refused_And_Cannot_Reopen_A_Running_Job_To_A_Second_Claim(string current)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, current);

        var result = await Handle(context, depositId, jobId, PipelineJobStates.Waiting);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        (await ReloadJob(jobId)).Status.Should().Be(current);

        var duplicateClaim = await Handle(context, depositId, jobId, PipelineJobStates.Running);
        duplicateClaim.ErrorCode.Should().Be(ErrorCodes.Conflict,
            "the job never went back to waiting, so a duplicate start message can't claim it again");
    }

    [Fact]
    public async Task A_Status_That_Is_Not_A_Pipeline_State_Is_Refused_And_Not_Written()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Running);

        var result = await Handle(context, depositId, jobId, "bogus");

        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        (await ReloadJob(jobId)).Status.Should().Be(PipelineJobStates.Running);
    }

    [Fact]
    public async Task MetadataCreated_On_A_Job_That_Was_Never_Claimed_Is_A_Conflict_And_It_Stays_Waiting()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.Waiting);

        var result = await Handle(context, depositId, jobId, PipelineJobStates.MetadataCreated);

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await ReloadJob(jobId)).Status.Should().Be(PipelineJobStates.Waiting);
    }

    [Fact]
    public async Task A_Repeated_MetadataCreated_Report_Is_Accepted()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, jobId) = await SeedJob(context, PipelineJobStates.MetadataCreated);

        var result = await Handle(context, depositId, jobId, PipelineJobStates.MetadataCreated);

        result.Success.Should().BeTrue("SNS/SQS can deliver the same report twice");
        (await ReloadJob(jobId)).Status.Should().Be(PipelineJobStates.MetadataCreated);
    }

    [Theory]
    [InlineData(PipelineJobStates.Completed)]
    [InlineData(PipelineJobStates.CompletedWithErrors)]
    public async Task A_Terminal_Report_For_A_Job_That_Does_Not_Exist_Is_NotFound(string terminalStatus)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, _) = await SeedJob(context, PipelineJobStates.Running);

        var result = await Handle(context, depositId, $"job-{Guid.NewGuid()}", terminalStatus);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound, "it used to throw from SingleAsync, a 500");
    }

    [Fact]
    public async Task An_Intermediate_Report_For_A_Job_That_Does_Not_Exist_Is_NotFound()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var (depositId, _) = await SeedJob(context, PipelineJobStates.Running);

        var result = await Handle(context, depositId, $"job-{Guid.NewGuid()}", PipelineJobStates.MetadataCreated);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
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
        Preservation.API.Data.PreservationContext context, string status,
        string runUser = "tester", string? depositLockedBy = null)
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
            LastModifiedBy = "tester",
            LockedBy = depositLockedBy,
            LockDate = depositLockedBy != null ? DateTime.UtcNow : null
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
            RunUser = runUser
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

    private async Task<DepositEntity> ReloadDeposit(string depositId)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        return await context.Deposits.AsNoTracking().SingleAsync(d => d.MintedId == depositId);
    }
}
