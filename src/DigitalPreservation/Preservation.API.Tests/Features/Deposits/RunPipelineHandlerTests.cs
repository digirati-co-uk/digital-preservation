using System.Net;
using System.Security.Claims;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using FakeItEasy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Preservation.API.Data;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Repository.Common;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits;

[Collection(DatabaseCollection.CollectionName)]
public class RunPipelineHandlerTests(DatabaseFixture fixture)
{
    private const string DefaultBucket = "dev-deposits";

    private static ClaimsPrincipal Tester() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "test"));

    private static RunPipelineHandler CreateHandler(
        PreservationContext context, IAmazonSimpleNotificationService snsClient)
    {
        var identityMinter = A.Fake<IIdentityMinter>();
        // A unique ID per call: this collection shares one real Postgres container across every
        // test in this class, and a fixed ID would collide on the PipelineRunJob primary key
        // the moment more than one test creates a job.
        A.CallTo(() => identityMinter.MintIdentity("PipelineJob", null))
            .ReturnsLazily(() => $"pipelinejob-test-{Guid.NewGuid()}");
        return new RunPipelineHandler(
            new NullLogger<RunPipelineHandler>(),
            context,
            snsClient,
            Options.Create(new PipelineOptions
            {
                PipelineJobTopicArn = "arn:aws:sns:eu-west-1:000000000000:test-topic",
                PipelineJobQueue = "test-queue"
            }),
            Options.Create(new AwsStorageOptions { DefaultWorkingBucket = DefaultBucket }),
            identityMinter);
    }

    private static async Task<DepositEntity> SaveDeposit(
        PreservationContext context, Uri? files, string? lockedBy = null)
    {
        var deposit = new DepositEntity
        {
            MintedId = $"dep-{Guid.NewGuid()}",
            Status = DepositStates.New,
            Active = true,
            Created = DateTime.UtcNow,
            CreatedBy = "tester",
            LastModified = DateTime.UtcNow,
            LastModifiedBy = "tester",
            Files = files,
            LockedBy = lockedBy,
            LockDate = lockedBy != null ? DateTime.UtcNow : null
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync();
        return deposit;
    }

    private async Task<DepositEntity> ReloadDeposit(string depositId)
    {
        // A fresh context: AcquireLock/ReleaseLockIfHeldBy run as conditional UPDATEs in the
        // database, invisible to any change tracker that loaded the row beforehand.
        await using var context = fixture.CreateNewAuthServiceContext();
        return await context.Deposits.AsNoTracking().SingleAsync(d => d.MintedId == depositId);
    }

    private static async Task SavePipelineRunJob(
        PreservationContext context, string depositId, string status, DateTime? dateSubmitted = null)
    {
        context.PipelineRunJobs.Add(new Preservation.API.Data.Entities.PipelineRunJob
        {
            Id = $"job-{Guid.NewGuid()}",
            Deposit = depositId,
            ArchivalGroup = null,
            Status = status,
            DateSubmitted = dateSubmitted ?? DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            PipelineJobJson = "{}",
            RunUser = "tester"
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_QueuesJob_ForDepositInDefaultWorkingBucket()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Returns(new PublishResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "m-1" });

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeTrue();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        context.PipelineRunJobs.Where(j => j.Deposit == deposit.MintedId && j.RunUser == "tester").Should().HaveCount(1);
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().Be("tester", "queueing the run must acquire the lock as the caller");
        reloaded.LockDate.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_Declines_DepositOutsideDefaultWorkingBucket()
    {
        // A deposit routed to a per-caller bucket (RFC-0001 §8) can't be characterised: the
        // pipeline reaches deposit files through a filesystem mount of the default bucket only.
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri("s3://leeds-goobi-deposits/deposits/dep-x/"));
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.PipelineRunJobs.Where(j => j.Deposit == deposit.MintedId).Should().BeEmpty();
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().BeNull("a bad-bucket 400 must never leave a lock behind");
    }

    [Fact]
    public async Task Handle_Refuses_ADepositLockedBySomeoneElse()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"), lockedBy: "someone-else");
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.PipelineRunJobs.Where(j => j.Deposit == deposit.MintedId).Should().BeEmpty();
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().Be("someone-else", "a refused run must not disturb the lock it lost to");
    }

    [Fact]
    public async Task Handle_Proceeds_WhenTheCallerAlreadyHoldsTheLock()
    {
        // Decided (Tom, 2026-09-23): a caller who already holds the lock has it transferred to
        // the run - no data change needed, since the run's identity is the caller's.
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"), lockedBy: "tester");
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Returns(new PublishResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "m-1" });

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeTrue();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().Be("tester", "the lock transfers to the run rather than being re-taken");
    }

    [Fact]
    public async Task Handle_ReleasesALockItAcquired_WhenPublishingToSnsFails()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Throws(new AmazonSimpleNotificationServiceException("SNS is unavailable"));

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnknownError);
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().BeNull("the lock this call took must not survive a failed publish");
        var job = context.PipelineRunJobs.Single(j => j.Deposit == deposit.MintedId);
        job.Status.Should().Be(PipelineJobStates.CompletedWithErrors);
    }

    /// <summary>
    /// The lock holder queuing a second run while the first is still going (a double-submit, a
    /// script, two browser tabs) would write to the same metadata/ tool-output folders twice, and
    /// run 1 finishing would release the lock run 2 relies on, since both share the same RunUser
    /// (issue #317). Checked and refused before the lock is even acquired, so a 409 here never
    /// leaves one behind.
    /// </summary>
    [Theory]
    [InlineData(PipelineJobStates.Waiting)]
    [InlineData(PipelineJobStates.Running)]
    [InlineData(PipelineJobStates.MetadataCreated)]
    public async Task Handle_Refuses_WhenAnotherJob_Is_Already_Active_For_The_Deposit(string activeStatus)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        await SavePipelineRunJob(context, deposit.MintedId, activeStatus);
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.PipelineRunJobs.Count(j => j.Deposit == deposit.MintedId).Should().Be(1,
            "no second job row is created for the refused run");
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().BeNull("the check runs before the lock is acquired, so a refusal leaves no lock behind");
    }

    /// <summary>
    /// Backstop for issue #353: the read-then-insert can't serialise the lock holder racing
    /// themselves (a double-click, a retrying script, two tabs). Deposit starts locked by the
    /// caller already (the "lock holder" in the issue title) so both calls transfer rather than
    /// acquire - neither is responsible for releasing on a lost race.
    /// NB this exercises two genuinely concurrent requests, but with no control over their
    /// relative timing it doesn't prove the unique index itself is what decided the winner - if
    /// one call's acquire-and-insert fully completes before the other even starts, the second call
    /// is refused by the ordinary read check at the top of Handle instead, giving the same
    /// one-success-one-conflict outcome asserted below without ever reaching the index. The
    /// deterministic case for the index doing the deciding is
    /// <see cref="Handle_ReleasesALockItAcquired_WhenItsInsertLosesTheRaceToAnotherActiveJob"/> and
    /// <see cref="Handle_DoesNotReleaseALockItAcquired_WhenItLosesTheRaceToTheSameCallersOwnJob"/>,
    /// which race the second insert in via an interceptor rather than real concurrency.
    /// </summary>
    [Fact]
    public async Task Only_One_Of_Two_Concurrent_Claims_By_The_Lock_Holder_Wins()
    {
        await using var seedContext = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(seedContext, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"), lockedBy: "tester");

        var snsClient = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Returns(new PublishResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "m-1" });

        // Separate contexts, so this is two concurrent requests racing rather than one change
        // tracker used twice.
        await using var first = fixture.CreateNewAuthServiceContext();
        await using var second = fixture.CreateNewAuthServiceContext();

        var results = await Task.WhenAll(
            CreateHandler(first, snsClient).Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None),
            CreateHandler(second, snsClient).Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None));

        results.Count(r => r.Success).Should().Be(1, "exactly one of the two double-submitted calls may queue a run");
        results.Single(r => !r.Success).ErrorCode.Should().Be(ErrorCodes.Conflict);
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        await using var assertContext = fixture.CreateNewAuthServiceContext();
        assertContext.PipelineRunJobs.Count(j => j.Deposit == deposit.MintedId).Should().Be(1,
            "the losing call's insert must not leave a second active job row");
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().Be("tester", "the lock holder's own race must not release the lock either way");
    }

    /// <summary>
    /// Simulates another active job appearing for the same deposit after this call's own read
    /// check has already passed, by inserting it via a raw connection from a
    /// <see cref="SaveChangesInterceptor"/> that fires just before this call's own insert - the
    /// same unique-index violation a true race would produce, but deterministic. This call took
    /// the lock from free (the deposit starts unlocked), and the racing job belongs to a different
    /// user, so it alone is responsible for releasing it when its insert loses.
    /// </summary>
    [Fact]
    public async Task Handle_ReleasesALockItAcquired_WhenItsInsertLosesTheRaceToAnotherActiveJob()
    {
        await using var seedContext = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(seedContext, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();

        var options = new DbContextOptionsBuilder<PreservationContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new InsertRacingActiveJobInterceptor(fixture.ConnectionString, deposit.MintedId, "someone-else"))
            .Options;
        await using var context = new PreservationContext(options);

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        await using var assertContext = fixture.CreateNewAuthServiceContext();
        var jobs = assertContext.PipelineRunJobs.Where(j => j.Deposit == deposit.MintedId).ToList();
        jobs.Should().HaveCount(1, "only the racing job inserted by the interceptor should exist - this call's own insert must have been rolled back");
        jobs.Single().RunUser.Should().Be("someone-else", "the surviving row is the one the interceptor raced in, not this call's own");
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().BeNull("this call took the lock from free, and the surviving job belongs to someone else, so this call alone must release it when its insert loses");
    }

    /// <summary>
    /// The race the adversarial review of #353 flagged as the serious case: this call takes the
    /// lock from free (acquiredByThisCall), but a same-user double-submit's own insert is the one
    /// that wins the unique index, not this call's. Which call set LockedBy and which call's insert
    /// survives aren't the same thing - the surviving job (inserted by the interceptor, RunUser
    /// "tester" like the caller) now owns the lock under issue #299's transfer rule, so this call
    /// must not release it out from under that still-running job just because it happened to be the
    /// one that physically acquired it.
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotReleaseALockItAcquired_WhenItLosesTheRaceToTheSameCallersOwnJob()
    {
        await using var seedContext = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(seedContext, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();

        var options = new DbContextOptionsBuilder<PreservationContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new InsertRacingActiveJobInterceptor(fixture.ConnectionString, deposit.MintedId, "tester"))
            .Options;
        await using var context = new PreservationContext(options);

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("racing-job-", "the backstop conflict should name the surviving job");
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        await using var assertContext = fixture.CreateNewAuthServiceContext();
        var jobs = assertContext.PipelineRunJobs.Where(j => j.Deposit == deposit.MintedId).ToList();
        jobs.Should().HaveCount(1, "only the racing job inserted by the interceptor should exist - this call's own insert must have been rolled back");
        jobs.Single().RunUser.Should().Be("tester");
        var reloaded = await ReloadDeposit(deposit.MintedId);
        reloaded.LockedBy.Should().Be("tester",
            "the surviving job belongs to the same caller, so the lock now belongs to that job, even though this call is the one that physically acquired it");
    }

    /// <summary>
    /// Races a conflicting active job into the database, via a raw connection, immediately before
    /// the handler's own insert of its new job row reaches the database - reproducing the
    /// unique-index violation a genuine double-submit would cause, deterministically.
    /// </summary>
    private sealed class InsertRacingActiveJobInterceptor(string connectionString, string depositId, string runUser) : SaveChangesInterceptor
    {
        private bool fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!fired)
            {
                fired = true;
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO pipeline_run_jobs (id, deposit, archival_group, status, last_updated, date_submitted, pipeline_job_json, run_user)
                    VALUES (@id, @deposit, NULL, 'waiting', now(), now(), '{}', @runUser)
                    """;
                command.Parameters.AddWithValue("id", $"racing-job-{Guid.NewGuid()}");
                command.Parameters.AddWithValue("deposit", depositId);
                command.Parameters.AddWithValue("runUser", runUser);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Handle_Runs_Normally_WhenTheDepositsOnlyJobsAreTerminal()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        await SavePipelineRunJob(context, deposit.MintedId, PipelineJobStates.Completed);
        await SavePipelineRunJob(context, deposit.MintedId, PipelineJobStates.CompletedWithErrors);
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Returns(new PublishResponse { HttpStatusCode = HttpStatusCode.OK, MessageId = "m-1" });

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeTrue("a deposit whose previous runs have all finished may run again");
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        context.PipelineRunJobs.Count(j => j.Deposit == deposit.MintedId).Should().Be(3);
    }

    /// <summary>
    /// Data predating issue #339's check (or the #301/#357 sweep catching up) can leave an old job
    /// non-terminal - processing, say - while a job submitted after it has already completed. Before
    /// #357 this blocked every new run indefinitely: the check looked at *any* active job regardless
    /// of recency, where the UI's "what's running" look-up only ever considered the latest-by-date
    /// job - so a non-latest active job was invisible to the UI (nothing to force-complete) and yet
    /// still refused here. Blocking only on the deposit's actual latest job fixes both: a stale,
    /// superseded job no longer blocks a new run (and the sweep now closes it immediately rather
    /// than waiting out its usual overdue cutoff, per issue #357).
    /// </summary>
    [Fact]
    public async Task Handle_Refuses_WhenANonLatestJobIsStillActive_EvenIfTheDepositsLatestJobIsComplete()
    {
        // Issue #357 review: a prior version of this handler blocked only on the deposit's *latest*
        // job, so this same seed data (a stale, non-latest active job alongside a newer completed
        // one) was asserted to succeed. That contradicts #353's partial unique index, which allows
        // at most one active job per deposit at all - this exact row combination cannot even be
        // inserted under that constraint, and a "latest only" read check here would pass, then hit
        // the index on insert as an unhandled constraint violation instead of a clean 409. Blocking
        // on ANY active job (restored to match main) keeps the read check and the index in
        // agreement, and matches the UI, which shows whichever job is still running. A stale
        // non-latest job like this is closed on the very next sweep tick (PipelineJobCleanupProcessor),
        // not left to block runs indefinitely.
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SaveDeposit(context, new Uri($"s3://{DefaultBucket}/deposits/dep-x/"));
        var earlier = DateTime.UtcNow.AddHours(-2);
        await SavePipelineRunJob(context, deposit.MintedId, PipelineJobStates.Running, dateSubmitted: earlier);
        await SavePipelineRunJob(context, deposit.MintedId, PipelineJobStates.Completed, dateSubmitted: earlier.AddMinutes(5));
        var snsClient = A.Fake<IAmazonSimpleNotificationService>();

        var result = await CreateHandler(context, snsClient)
            .Handle(new RunPipeline(deposit.MintedId, Tester()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        A.CallTo(() => snsClient.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.PipelineRunJobs.Count(j => j.Deposit == deposit.MintedId).Should().Be(2);
    }
}
