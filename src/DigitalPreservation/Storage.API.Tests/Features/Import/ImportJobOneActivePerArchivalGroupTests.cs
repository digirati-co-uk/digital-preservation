using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Storage.API.Data;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Data;
using Storage.API.Features.Import.Requests;
using Storage.API.Tests.TestingInfrastructure;
using ImportJobEntity = Storage.API.Data.Entities.ImportJob;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// Issue #460: QueueImportJobHandler checks for an active job and then inserts, so two requests
/// arriving together could both pass the check and both queue. A partial unique index now allows
/// one active job per Archival Group, and the loser of the race gets the same 409 as the check.
/// Real Postgres, because the index is what's under test. Every test uses its own Archival Group.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class ImportJobOneActivePerArchivalGroupTests(DatabaseFixture fixture)
{
    private const string MigrationBeforeTheIndex = "20261008130359_Add LastHeartbeat to ImportJob";
    private static readonly Uri Agent = new("https://storage.test/agents/someone");

    private static readonly Converters TestStorageConverters = new(
        Options.Create(new FedoraOptions
        {
            Root = new Uri("https://fedora.test/fcrepo/rest/"),
            AdminUser = "admin",
            AdminPassword = "admin",
            Bucket = "fedora-bucket",
            OcflS3Prefix = ""
        }),
        Options.Create(new ConverterOptions { StorageRoot = new Uri("https://storage.test/repository/") }));

    private static Uri NewArchivalGroup() => new($"https://storage.test/repository/cc/{Guid.NewGuid()}");

    private static ImportJobResultStore CreateStore(StorageContext dbContext) => new(
        dbContext, NullLogger<ImportJobResultStore>.Instance, Options.Create(new ImportJobOptions()), TestStorageConverters);

    private async Task<List<ImportJobEntity>> JobsFor(Uri archivalGroup)
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        return await dbContext.ImportJobs.AsNoTracking()
            .Where(ij => ij.ArchivalGroup == archivalGroup)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_Second_Active_Job_For_The_Same_Group_Is_Refused_With_Conflict()
    {
        var archivalGroup = NewArchivalGroup();
        var importJob = new ImportJob { ArchivalGroup = archivalGroup };
        await using var dbContext = fixture.CreateNewStorageContext();
        var store = CreateStore(dbContext);

        var first = await store.SaveImportJob($"first-{Guid.NewGuid()}", importJob, CancellationToken.None);
        var second = await store.SaveImportJob($"second-{Guid.NewGuid()}", importJob, CancellationToken.None);

        first.Success.Should().BeTrue();
        second.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await JobsFor(archivalGroup)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_Job_Can_Be_Saved_Once_The_Group_Has_No_Active_Job()
    {
        var archivalGroup = NewArchivalGroup();
        var importJob = new ImportJob { ArchivalGroup = archivalGroup };
        await using var dbContext = fixture.CreateNewStorageContext();
        var store = CreateStore(dbContext);
        var firstId = $"first-{Guid.NewGuid()}";
        (await store.SaveImportJob(firstId, importJob, CancellationToken.None)).Success.Should().BeTrue();
        await dbContext.ImportJobs.Where(ij => ij.Id == firstId)
            .ExecuteUpdateAsync(s => s.SetProperty(ij => ij.Active, false), TestContext.Current.CancellationToken);

        var second = await store.SaveImportJob($"second-{Guid.NewGuid()}", importJob, CancellationToken.None);

        second.Success.Should().BeTrue();
        (await JobsFor(archivalGroup)).Should().HaveCount(2).And.ContainSingle(ij => ij.Active);
    }

    [Fact]
    public async Task Different_Groups_Each_Have_Their_Own_Active_Job()
    {
        await using var dbContext = fixture.CreateNewStorageContext();
        var store = CreateStore(dbContext);

        var first = await store.SaveImportJob($"a-{Guid.NewGuid()}", new ImportJob { ArchivalGroup = NewArchivalGroup() }, CancellationToken.None);
        var second = await store.SaveImportJob($"b-{Guid.NewGuid()}", new ImportJob { ArchivalGroup = NewArchivalGroup() }, CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Two_Concurrent_Queue_Requests_For_One_Group_Give_One_Queued_Job_And_One_Conflict()
    {
        var archivalGroup = NewArchivalGroup();
        var importJobQueue = A.Fake<IImportJobQueue>();

        // Each request reads "no active job", then waits until the other has read too, so both pass
        // the check before either inserts: the race the index exists for, every time.
        var reads = 0;
        var bothHaveRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var contextA = fixture.CreateNewStorageContext();
        await using var contextB = fixture.CreateNewStorageContext();
        QueueImportJobHandler CreateHandler(StorageContext dbContext, string jobId)
        {
            var realStore = CreateStore(dbContext);
            var store = A.Fake<IImportJobResultStore>(o => o.Wrapping(realStore));
            A.CallTo(() => store.GetActiveJobsForArchivalGroup(A<Uri>._, A<CancellationToken>._))
                .ReturnsLazily(async (Uri group, CancellationToken token) =>
                {
                    var active = await realStore.GetActiveJobsForArchivalGroup(group, token);
                    if (Interlocked.Increment(ref reads) == 2)
                    {
                        bothHaveRead.SetResult();
                    }
                    await bothHaveRead.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
                    return active;
                });
            var identityMinter = A.Fake<IIdentityMinter>();
            A.CallTo(() => identityMinter.MintIdentity(A<string>._, A<Uri>._)).Returns(jobId);
            return new QueueImportJobHandler(NullLogger<QueueImportJobHandler>.Instance,
                TestConverters.Make(storageRoot: new Uri("https://storage.test/repository/")),
                identityMinter, store, importJobQueue);
        }
        var handlerA = CreateHandler(contextA, $"race-a-{Guid.NewGuid()}");
        var handlerB = CreateHandler(contextB, $"race-b-{Guid.NewGuid()}");
        ImportJob NewImportJob() => new() { ArchivalGroup = archivalGroup, LastModifiedBy = Agent };

        var results = await Task.WhenAll(
            Task.Run(() => handlerA.Handle(new QueueImportJob(NewImportJob()), CancellationToken.None), TestContext.Current.CancellationToken),
            Task.Run(() => handlerB.Handle(new QueueImportJob(NewImportJob()), CancellationToken.None), TestContext.Current.CancellationToken));

        reads.Should().Be(2, "both requests must have passed the check for this to test the race");
        results.Should().ContainSingle(r => r.Success);
        results.Should().ContainSingle(r => r.Failure).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        A.CallTo(() => importJobQueue.QueueRequest(A<string>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        (await JobsFor(archivalGroup)).Should().ContainSingle().Which.Active.Should().BeTrue();
    }

    [Fact]
    public async Task The_Migration_Ends_Existing_Duplicates_Keeping_The_Job_Most_Likely_To_Be_Real()
    {
        // A database of its own, migrated to just before the index, so duplicates can be seeded.
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}"
        }.ConnectionString;
        await using var dbContext = new StorageContext(new DbContextOptionsBuilder<StorageContext>()
            .UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeTheIndex, TestContext.Current.CancellationToken);

        var duplicated = NewArchivalGroup();
        var alone = NewArchivalGroup();
        var now = DateTime.UtcNow;
        dbContext.ImportJobs.AddRange(
            Job("running", duplicated, received: now.AddMinutes(-30), heartbeat: now.AddMinutes(-1)),
            Job("waiting-newer", duplicated, received: now.AddMinutes(-5), heartbeat: null,
                errors: [new Error { Message = "an earlier problem" }]),
            Job("no-result", duplicated, received: now.AddMinutes(-10), heartbeat: null, withResult: false),
            Job("finished", duplicated, received: now.AddHours(-2), heartbeat: null, active: false),
            Job("alone", alone, received: now.AddMinutes(-5), heartbeat: null));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        var jobs = await dbContext.ImportJobs.AsNoTracking().ToDictionaryAsync(ij => ij.Id, TestContext.Current.CancellationToken);
        jobs["running"].Active.Should().BeTrue("a job with a heartbeat is the one actually running");
        jobs["alone"].Active.Should().BeTrue();
        jobs["finished"].EndTime.Should().BeNull("an inactive job is left as it was");

        jobs["waiting-newer"].Active.Should().BeFalse();
        jobs["waiting-newer"].EndTime.Should().NotBeNull();
        var ended = JsonSerializer.Deserialize<ImportJobResult>(jobs["waiting-newer"].ImportJobResultJson!)!;
        ended.Status.Should().Be(ImportJobStates.CompletedWithErrors);
        ended.DateFinished.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        ended.Errors!.Select(e => e.Message).Should().HaveCount(2)
            .And.StartWith("an earlier problem")
            .And.Contain(m => m.Contains("#460"));
        ended.ArchivalGroup.Should().Be(duplicated, "the rest of the stored result is kept");

        jobs["no-result"].Active.Should().BeFalse();
        jobs["no-result"].ImportJobResultJson.Should().BeNull();

        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    private static ImportJobEntity Job(string id, Uri archivalGroup, DateTime received, DateTime? heartbeat,
        bool active = true, bool withResult = true, Error[]? errors = null) => new()
    {
        Id = id,
        ArchivalGroup = archivalGroup,
        ImportJobJson = "{}",
        ImportJobResultJson = withResult
            ? JsonSerializer.Serialize(new ImportJobResult
            {
                Id = new Uri($"https://storage.test/import/results/{id}"),
                ImportJob = new Uri($"https://storage.test/import/{id}"),
                ArchivalGroup = archivalGroup,
                Status = heartbeat == null ? ImportJobStates.Waiting : ImportJobStates.Running,
                Errors = errors
            })
            : null,
        Active = active,
        Received = received,
        LastHeartbeat = heartbeat
    };
}
