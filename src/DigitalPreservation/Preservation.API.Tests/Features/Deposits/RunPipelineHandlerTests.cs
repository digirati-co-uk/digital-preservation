using System.Net;
using System.Security.Claims;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using FakeItEasy;
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
        A.CallTo(() => identityMinter.MintIdentity("PipelineJob", null)).Returns("pipelinejob-test");
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

    private async Task<DepositEntity> SaveDeposit(PreservationContext context, Uri? files)
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
            Files = files
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync();
        return deposit;
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
    }
}
