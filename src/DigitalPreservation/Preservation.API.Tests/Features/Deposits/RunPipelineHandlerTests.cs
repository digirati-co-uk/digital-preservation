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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
}
