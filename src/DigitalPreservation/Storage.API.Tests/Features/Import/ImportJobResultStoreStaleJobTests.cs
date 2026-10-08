using System.Text.Json;
using DigitalPreservation.Common.Model.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Features.Import.Data;
using Storage.API.Tests.TestingInfrastructure;
using ImportJobEntity = Storage.API.Data.Entities.ImportJob;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// GetActiveJobsForArchivalGroup reaps a running job (one that has sent at least one heartbeat)
/// whose heartbeat has gone stale, so a processor that crashed or was killed mid-run (e.g. an ECS
/// task restart) doesn't wedge its Archival Group's imports behind a 409 Conflict forever. A job
/// that has never sent a heartbeat - still queued - is never reaped by age here: the message may
/// genuinely still be in the queue (SQS retains it for up to 14 days).
///
/// Runs against real Postgres (Testcontainers), not EF InMemory: the reap is a conditional
/// ExecuteUpdateAsync guarded by the database's own clock, which the InMemory provider cannot run.
/// All tests share one Postgres container (and some methods, like FailOrphanedWaitingJobs, act
/// across every row regardless of Archival Group), so every test uses its own freshly-generated
/// Archival Group and job id, and assertions about "other" data stay loose rather than exact.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class ImportJobResultStoreStaleJobTests(DatabaseFixture fixture)
{
    private static readonly IConfiguration DefaultConfiguration = new ConfigurationBuilder().Build();

    private static Uri NewArchivalGroup() => new($"https://storage.test/repository/cc/{Guid.NewGuid()}");

    private static ImportJobEntity BuildJob(string id, DateTime? lastHeartbeat, Uri archivalGroup)
    {
        var jobResult = new ImportJobResult
        {
            Id = new Uri($"https://storage.test/import/results/{id}"),
            ImportJob = new Uri($"https://storage.test/import/{id}"),
            ArchivalGroup = archivalGroup,
            Status = lastHeartbeat == null ? ImportJobStates.Waiting : ImportJobStates.Running,
            DateBegun = lastHeartbeat == null ? null : DateTime.UtcNow.AddMinutes(-20)
        };
        return new ImportJobEntity
        {
            Id = id,
            ArchivalGroup = archivalGroup,
            ImportJobJson = "{}",
            ImportJobResultJson = JsonSerializer.Serialize(jobResult),
            Active = true,
            Received = DateTime.UtcNow.AddMinutes(-20),
            LastHeartbeat = lastHeartbeat
        };
    }

    [Fact]
    public async Task A_Heartbeating_Long_Running_Job_Is_Never_Reaped()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var job = BuildJob("heartbeating-job", DateTime.UtcNow.AddMinutes(-1), NewArchivalGroup());
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        var result = await sut.GetActiveJobsForArchivalGroup(job.ArchivalGroup, CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Should().Be("heartbeating-job");
    }

    [Fact]
    public async Task A_Job_With_No_Heartbeat_Is_Never_Reaped_By_Age_Alone()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var job = BuildJob("queued-job", null, NewArchivalGroup());
        job.Received = DateTime.UtcNow.AddDays(-7); // old, but still well inside SQS's 14-day retention
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        var result = await sut.GetActiveJobsForArchivalGroup(job.ArchivalGroup, CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Should().Be("queued-job",
            "a job that has never sent a heartbeat may simply still be queued");
    }

    [Fact]
    public async Task A_Job_With_A_Stale_Heartbeat_Is_Reaped_And_Recorded_As_Failed()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var job = BuildJob("abandoned-job", DateTime.UtcNow.AddMinutes(-15), NewArchivalGroup()); // older than the 10-minute default window
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        var result = await sut.GetActiveJobsForArchivalGroup(job.ArchivalGroup, CancellationToken.None);

        result.Value.Should().BeEmpty("the job's heartbeat is older than the staleness window, so it should no longer block new imports");

        await using var verifyContext = fixture.CreateNewStorageContext();
        var reaped = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "abandoned-job");
        reaped.Active.Should().BeFalse("reaping should persist, so repeated checks don't keep finding the same stale row");
        reaped.EndTime.Should().NotBeNull();
        reaped.ImportJobResultJson.Should().Contain(ImportJobStates.CompletedWithErrors);

        var activity = await sut.GetActivityPageOfResults(1, 1000, CancellationToken.None);
        activity.Value.Should().Contain(a => a.Object.SeeAlso!.Any(s => s.Id == job.ArchivalGroup),
            "a reaped job must still appear in the import-job activity stream, so the Preservation API learns it finished");
    }

    [Fact]
    public async Task A_Reaped_Jobs_Final_Write_From_The_Runner_Does_Not_Overwrite_The_Reapers_Record()
    {
        // Simulates a job that was reaped (e.g. a long GC pause stalled its heartbeat past the
        // window) while actually still alive, which then resumes and tries to save its own
        // "completed" result. That write must be a no-op: the reaper's record must stand.
        await using var dbContext = fixture.CreateNewStorageContext();
        var archivalGroup = NewArchivalGroup();
        var job = BuildJob("already-reaped-job", DateTime.UtcNow.AddMinutes(-15), archivalGroup);
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);
        await sut.GetActiveJobsForArchivalGroup(archivalGroup, CancellationToken.None); // reaps it

        var lateResult = new ImportJobResult
        {
            Id = new Uri("https://storage.test/import/results/already-reaped-job"),
            ImportJob = new Uri("https://storage.test/import/already-reaped-job"),
            ArchivalGroup = archivalGroup,
            Status = ImportJobStates.Completed,
            DateFinished = DateTime.UtcNow
        };
        var saveResult = await sut.SaveFinalImportJobResult("already-reaped-job", lateResult, CancellationToken.None);

        saveResult.Success.Should().BeTrue();
        saveResult.Value.Should().BeFalse("the row was no longer Active, so the late write must not have applied");

        await using var verifyContext = fixture.CreateNewStorageContext();
        var stillReaped = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "already-reaped-job");
        stillReaped.ImportJobResultJson.Should().Contain(ImportJobStates.CompletedWithErrors,
            "the reaper's record must survive, not the late 'completed' result");
    }

    [Fact]
    public async Task A_Reaped_Jobs_Running_Write_From_The_Executor_Does_Not_Resurrect_It()
    {
        // Simulates a job that was reaped (e.g. a long GC pause or debugger break stalled its
        // heartbeat past the window) just before ExecuteImportJobHandler reaches its own "still
        // running" write, which happens before anything touches Fedora. That write must be a no-op:
        // the reaper's record must stand, and the row must not flip back to Active.
        await using var dbContext = fixture.CreateNewStorageContext();
        var archivalGroup = NewArchivalGroup();
        var job = BuildJob("already-reaped-before-running-write", DateTime.UtcNow.AddMinutes(-15), archivalGroup);
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);
        await sut.GetActiveJobsForArchivalGroup(archivalGroup, CancellationToken.None); // reaps it

        var lateRunningResult = new ImportJobResult
        {
            Id = new Uri("https://storage.test/import/results/already-reaped-before-running-write"),
            ImportJob = new Uri("https://storage.test/import/already-reaped-before-running-write"),
            ArchivalGroup = archivalGroup,
            Status = ImportJobStates.Running,
            SourceVersion = "v3"
        };
        var saveResult = await sut.SaveRunningImportJobResult(
            "already-reaped-before-running-write", lateRunningResult, CancellationToken.None);

        saveResult.Success.Should().BeTrue();
        saveResult.Value.Should().BeFalse("the row was no longer Active, so the late running write must not have applied");

        await using var verifyContext = fixture.CreateNewStorageContext();
        var stillReaped = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "already-reaped-before-running-write");
        stillReaped.Active.Should().BeFalse("the running write must not resurrect a reaped row");
        stillReaped.ImportJobResultJson.Should().Contain(ImportJobStates.CompletedWithErrors,
            "the reaper's record must survive, not the late 'running' result");
    }

    [Fact]
    public async Task ReapAllStaleRunningJobs_Reaps_A_Stale_Job_With_No_QueueImportJob_Call()
    {
        // The point of ImportJobReapService: a group whose only job was abandoned has nothing else
        // ever asking GetActiveJobsForArchivalGroup about it, so only this all-groups sweep can
        // unblock it without a human intervening.
        await using var dbContext = fixture.CreateNewStorageContext();
        var archivalGroup = NewArchivalGroup();
        var job = BuildJob("sweep-reaped-job", DateTime.UtcNow.AddMinutes(-15), archivalGroup); // older than the 10-minute default window
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        var result = await sut.ReapAllStaleRunningJobs(CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(1);

        await using var verifyContext = fixture.CreateNewStorageContext();
        var reaped = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "sweep-reaped-job");
        reaped.Active.Should().BeFalse("the sweep must reap a stale job without any call to GetActiveJobsForArchivalGroup");
        reaped.ImportJobResultJson.Should().Contain(ImportJobStates.CompletedWithErrors);
    }

    [Fact]
    public async Task ReapAllStaleRunningJobs_Leaves_A_Heartbeating_Job_Alone()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var job = BuildJob("sweep-heartbeating-job", DateTime.UtcNow.AddMinutes(-1), NewArchivalGroup());
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        var result = await sut.ReapAllStaleRunningJobs(CancellationToken.None);

        result.Success.Should().BeTrue();

        await using var verifyContext = fixture.CreateNewStorageContext();
        var stillRunning = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "sweep-heartbeating-job");
        stillRunning.Active.Should().BeTrue("a job heartbeating within the window must not be reaped by the sweep");
    }

    [Fact]
    public async Task FailOrphanedWaitingJobs_Fails_A_Waiting_Job_And_It_Appears_In_The_Activity_Stream()
    {
        // The in-process equivalent of a dead-lettered SQS message: the in-memory queue is lost on
        // restart, so anything still waiting (no heartbeat - never started) cannot be in the new,
        // empty channel. Called once at Storage API startup, only in in-process mode - SQS mode
        // never calls this at all, since there a waiting job may genuinely still be queued; that
        // distinction is made by Program.cs's feature-flag check, not by anything in this method,
        // so there is no separate "SQS mode leaves it alone" code path here to test.
        //
        // This method acts across every Active, never-heartbeated row regardless of Archival Group
        // (that's the point - it's a startup sweep), so unlike the other tests here it cannot be
        // isolated by using a fresh Archival Group alone; only this job's own row is asserted on.
        await using var dbContext = fixture.CreateNewStorageContext();
        var job = BuildJob("orphaned-waiting-job", null, NewArchivalGroup());
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        var result = await sut.FailOrphanedWaitingJobs(
            "never started: the in-process import queue was lost when the Storage API restarted", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(1);

        await using var verifyContext = fixture.CreateNewStorageContext();
        var failed = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "orphaned-waiting-job");
        failed.Active.Should().BeFalse();
        failed.EndTime.Should().NotBeNull();
        failed.ImportJobResultJson.Should().Contain(ImportJobStates.CompletedWithErrors);

        var activity = await sut.GetActivityPageOfResults(1, 1000, CancellationToken.None);
        activity.Value.Should().Contain(a => a.Object.SeeAlso!.Any(s => s.Id == job.ArchivalGroup));
    }

    [Fact]
    public async Task Heartbeat_Updates_An_Active_Jobs_Timestamp_But_Not_An_Inactive_Ones()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var activeJob = BuildJob("active-for-heartbeat", DateTime.UtcNow.AddMinutes(-5), NewArchivalGroup());
        var inactiveJob = BuildJob("inactive-for-heartbeat", DateTime.UtcNow.AddMinutes(-5), NewArchivalGroup());
        inactiveJob.Active = false;
        dbContext.ImportJobs.AddRange(activeJob, inactiveJob);
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance, DefaultConfiguration);

        await sut.Heartbeat("active-for-heartbeat", CancellationToken.None);
        await sut.Heartbeat("inactive-for-heartbeat", CancellationToken.None);

        await using var verifyContext = fixture.CreateNewStorageContext();
        var active = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "active-for-heartbeat");
        var inactive = await verifyContext.ImportJobs.SingleAsync(ij => ij.Id == "inactive-for-heartbeat");
        active.LastHeartbeat.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        // Postgres' timestamp precision (microseconds) is coarser than a .NET DateTime tick, so an
        // unchanged value can differ from what was written by a sub-microsecond rounding error on
        // round-trip - a tolerance, not an exact Be(), is the correct check for "did not change".
        inactive.LastHeartbeat.Should().BeCloseTo(inactiveJob.LastHeartbeat!.Value, TimeSpan.FromMilliseconds(1),
            "a heartbeat for a job that is no longer active must be a no-op");
    }
}
