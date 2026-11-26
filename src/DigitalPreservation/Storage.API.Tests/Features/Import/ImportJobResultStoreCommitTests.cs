using DigitalPreservation.Common.Model;
using System.Text.Json;
using DigitalPreservation.Common.Model.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Data;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Data;
using Storage.API.Tests.TestingInfrastructure;
using ImportJobEntity = Storage.API.Data.Entities.ImportJob;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// Issue #444: a job records that its commit started, and a stale job with that record is left for
/// the reconciler by both reap paths instead of being failed. Real Postgres, like the stale-job tests,
/// because every write here is guarded by the database's own clock.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class ImportJobResultStoreCommitTests(DatabaseFixture fixture)
{
    private static readonly Uri Transaction = new("https://fedora.test/fcrepo/rest/fcr:tx/abc");

    private ImportJobResultStore CreateStore(StorageContext dbContext) => new(
        dbContext, NullLogger<ImportJobResultStore>.Instance, Options.Create(new ImportJobOptions()),
        new Converters(
            Options.Create(new FedoraOptions
            {
                Root = new Uri("https://fedora.test/fcrepo/rest/"),
                AdminUser = "admin",
                AdminPassword = "admin",
                Bucket = "fedora-bucket",
                OcflS3Prefix = ""
            }),
            Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/repository/") })));

    private static (ImportJobEntity Entity, ImportJobResult Result) BuildJob(DateTime lastHeartbeat, DateTime? commitStarted = null)
    {
        var id = Guid.NewGuid().ToString();
        var archivalGroup = new Uri($"https://storage.test/repository/cc/{Guid.NewGuid()}");
        var result = new ImportJobResult
        {
            Id = new Uri($"https://storage.test/import/results/{id}"),
            ImportJob = new Uri($"https://storage.test/import/{id}"),
            ArchivalGroup = archivalGroup,
            Status = ImportJobStates.Running,
            SourceVersion = "v1"
        };
        var importJob = new ImportJob { ArchivalGroup = archivalGroup, IsUpdate = true };
        return (new ImportJobEntity
        {
            Id = id,
            ArchivalGroup = archivalGroup,
            ImportJobJson = JsonSerializer.Serialize(importJob),
            ImportJobResultJson = JsonSerializer.Serialize(result),
            Active = true,
            Received = DateTime.UtcNow.AddMinutes(-30),
            LastHeartbeat = lastHeartbeat,
            CommitStarted = commitStarted,
            CommitTransaction = commitStarted == null ? null : Transaction
        }, result);
    }

    private async Task<ImportJobEntity> Reload(string id)
    {
        await using var verifyContext = fixture.CreateNewStorageContext();
        return await verifyContext.ImportJobs.AsNoTracking().SingleAsync(ij => ij.Id == id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Recording_The_Commit_Saves_The_Transaction_The_Time_And_The_Lists()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (job, result) = BuildJob(DateTime.UtcNow);
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        result.BinariesAdded.Add(new Binary { Id = new Uri(job.ArchivalGroup + "/a.txt"), Name = "a.txt" });

        var recorded = await CreateStore(dbContext).RecordCommitStarted(job.Id, result, Transaction, CancellationToken.None);

        recorded.Value.Should().BeTrue();
        var saved = await Reload(job.Id);
        saved.CommitStarted.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        saved.CommitTransaction.Should().Be(Transaction);
        JsonSerializer.Deserialize<ImportJobResult>(saved.ImportJobResultJson!)!.BinariesAdded.Should().ContainSingle();
    }

    [Fact]
    public async Task Recording_The_Commit_Of_A_Job_Already_Reaped_Returns_False()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (job, result) = BuildJob(DateTime.UtcNow);
        job.Active = false;
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var recorded = await CreateStore(dbContext).RecordCommitStarted(job.Id, result, Transaction, CancellationToken.None);

        recorded.Value.Should().BeFalse();
        (await Reload(job.Id)).CommitStarted.Should().BeNull();
    }

    [Fact]
    public async Task The_Sweep_Does_Not_Reap_A_Stale_Job_Whose_Commit_Started()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (job, _) = BuildJob(DateTime.UtcNow.AddMinutes(-15), commitStarted: DateTime.UtcNow.AddMinutes(-16));
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateStore(dbContext).ReapAllStaleRunningJobs(CancellationToken.None);

        (await Reload(job.Id)).Active.Should().BeTrue("Fedora may have finished its commit: the reconciler decides");
    }

    [Fact]
    public async Task The_Inline_Reap_Leaves_A_Stale_Committing_Job_Active_And_Blocking_Its_Group()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (job, _) = BuildJob(DateTime.UtcNow.AddMinutes(-15), commitStarted: DateTime.UtcNow.AddMinutes(-16));
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var active = await CreateStore(dbContext).GetActiveJobsForArchivalGroup(job.ArchivalGroup, CancellationToken.None);

        active.Value.Should().ContainSingle().Which.Should().Be(job.Id);
        (await Reload(job.Id)).Active.Should().BeTrue();
    }

    [Fact]
    public async Task A_Stale_Committing_Job_Is_Listed_For_Reconciliation_But_A_Live_One_Is_Not()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (stale, _) = BuildJob(DateTime.UtcNow.AddMinutes(-15), commitStarted: DateTime.UtcNow.AddMinutes(-16));
        var (live, _) = BuildJob(DateTime.UtcNow, commitStarted: DateTime.UtcNow.AddMinutes(-16));
        dbContext.ImportJobs.AddRange(stale, live);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var jobs = await CreateStore(dbContext).GetStaleCommittingJobs(CancellationToken.None);

        var ids = jobs.Value!.Select(j => j.Id).ToList();
        ids.Should().Contain(stale.Id).And.NotContain(live.Id);
        var listed = jobs.Value!.Single(j => j.Id == stale.Id);
        listed.CommitTransaction.Should().Be(Transaction);
        listed.ImportJob.IsUpdate.Should().BeTrue();
        listed.ImportJobResult.SourceVersion.Should().Be("v1");
    }

    [Fact]
    public async Task The_Reconciled_Result_Ends_The_Job()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (job, result) = BuildJob(DateTime.UtcNow.AddMinutes(-15), commitStarted: DateTime.UtcNow.AddMinutes(-16));
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        result.Status = ImportJobStates.Completed;
        result.NewVersion = "v2";
        result.DateFinished = DateTime.UtcNow;

        var saved = await CreateStore(dbContext).SaveReconciledImportJobResult(job.Id, result, CancellationToken.None);

        saved.Value.Should().BeTrue();
        var reloaded = await Reload(job.Id);
        reloaded.Active.Should().BeFalse();
        reloaded.EndTime.Should().NotBeNull();
        JsonSerializer.Deserialize<ImportJobResult>(reloaded.ImportJobResultJson!)!.NewVersion.Should().Be("v2");
    }

    [Fact]
    public async Task The_Reconciled_Result_Is_Not_Saved_Over_A_Job_Whose_Heartbeat_Came_Back()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var (job, result) = BuildJob(DateTime.UtcNow, commitStarted: DateTime.UtcNow.AddMinutes(-16));
        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        result.Status = ImportJobStates.CompletedWithErrors;
        result.DateFinished = DateTime.UtcNow;

        var saved = await CreateStore(dbContext).SaveReconciledImportJobResult(job.Id, result, CancellationToken.None);

        saved.Value.Should().BeFalse("the runner was only paused, and its own result must win");
        (await Reload(job.Id)).Active.Should().BeTrue();
    }
}
