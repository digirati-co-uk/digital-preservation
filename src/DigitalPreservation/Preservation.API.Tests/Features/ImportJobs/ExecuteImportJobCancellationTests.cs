using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using LeedsDlipServices.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Client;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// Issue #428: once ExecuteImportJobHandler hands a job to the Storage API, the job may be running
/// whether or not the caller is still listening. A client disconnect from that point on must not
/// stop the Preservation API recording it, or the running job is orphaned: never polled, never
/// shown, never in the Activity Stream. A disconnect BEFORE the hand-off still cancels cleanly.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class ExecuteImportJobCancellationTests(DatabaseFixture fixture)
{
    private const string Caller = "tester";

    private static ClaimsPrincipal CallerPrincipal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, Caller)], "test"));

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));

    private async Task<DepositEntity> SeedDeposit(Preservation.API.Data.PreservationContext context)
    {
        var deposit = new DepositEntity
        {
            MintedId = $"dep-{Guid.NewGuid()}",
            Status = DepositStates.New,
            Active = true,
            Created = DateTime.UtcNow,
            CreatedBy = Caller,
            LastModified = DateTime.UtcNow,
            LastModifiedBy = Caller
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return deposit;
    }

    private static ImportJob JobFor(string mintedId) => new()
    {
        ArchivalGroup = new Uri("https://preservation.test/repository/cc/thing"),
        Deposit = new Uri($"https://preservation.test/deposits/{mintedId}")
    };

    private static ImportJobResult AcceptedByStorage(string storageJobId) => new()
    {
        Id = new Uri($"https://storage.test/import/results/{storageJobId}/cc/thing"),
        ImportJob = new Uri($"https://storage.test/import/{storageJobId}"),
        ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
        Status = ImportJobStates.Waiting
    };

    private static ExecuteImportJobHandler Handler(
        Preservation.API.Data.PreservationContext context, IStorageApiClient storageApi, string mintedId)
    {
        var identityService = A.Fake<IIdentityService>();
        A.CallTo(() => identityService.MintIdentity(A<string>._)).Returns(mintedId);
        return new ExecuteImportJobHandler(
            new NullLogger<ExecuteImportJobHandler>(), storageApi, context, identityService, Mutator());
    }

    [Fact]
    public async Task A_Disconnect_During_The_Storage_Call_Still_Records_The_Accepted_Job()
    {
        await using var context = fixture.CreateNewPreservationContext();
        var deposit = await SeedDeposit(context);
        var mintedId = "job-" + Guid.NewGuid();
        using var requestAborted = new CancellationTokenSource();
        var storageApi = A.Fake<IStorageApiClient>();
        CancellationToken tokenGivenToStorage = default;
        A.CallTo(() => storageApi.ExecuteImportJob(A<ImportJob>._, A<CancellationToken>._))
            .ReturnsLazily((ImportJob _, CancellationToken token) =>
            {
                // The Storage API accepts the job; the client goes away while the response is in flight.
                tokenGivenToStorage = token;
                requestAborted.Cancel();
                return Result.OkNotNull(AcceptedByStorage("storage-job-1"));
            });

        var result = await Handler(context, storageApi, mintedId).Handle(
            new ExecuteImportJob(JobFor(deposit.MintedId), CallerPrincipal()), requestAborted.Token);

        result.Success.Should().BeTrue();
        tokenGivenToStorage.CanBeCanceled.Should().BeFalse(
            "once sent, the Storage API may accept the job whether or not we are still listening");
        await using var verify = fixture.CreateNewPreservationContext();
        var saved = await verify.ImportJobs.SingleOrDefaultAsync(j => j.Id == mintedId, TestContext.Current.CancellationToken);
        saved.Should().NotBeNull("a job the Storage API accepted must not be orphaned by a client disconnect");
        saved!.StorageImportJobResultId.Should().Be(AcceptedByStorage("storage-job-1").Id!);
        saved.Deposit.Should().Be(deposit.MintedId);
    }

    [Fact]
    public async Task A_Disconnect_Before_The_Hand_Off_Cancels_Without_Calling_The_Storage_API()
    {
        await using var context = fixture.CreateNewPreservationContext();
        var deposit = await SeedDeposit(context);
        var mintedId = "job-" + Guid.NewGuid();
        var storageApi = A.Fake<IStorageApiClient>();
        using var requestAborted = new CancellationTokenSource();
        await requestAborted.CancelAsync();

        var act = () => Handler(context, storageApi, mintedId).Handle(
            new ExecuteImportJob(JobFor(deposit.MintedId), CallerPrincipal()), requestAborted.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        A.CallTo(() => storageApi.ExecuteImportJob(A<ImportJob>._, A<CancellationToken>._)).MustNotHaveHappened();
        await using var verify = fixture.CreateNewPreservationContext();
        (await verify.ImportJobs.AnyAsync(j => j.Id == mintedId, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task A_Job_The_Storage_API_Refuses_Is_Not_Recorded()
    {
        await using var context = fixture.CreateNewPreservationContext();
        var deposit = await SeedDeposit(context);
        var mintedId = "job-" + Guid.NewGuid();
        var storageApi = A.Fake<IStorageApiClient>();
        A.CallTo(() => storageApi.ExecuteImportJob(A<ImportJob>._, A<CancellationToken>._))
            .Returns(Result.FailNotNull<ImportJobResult>(ErrorCodes.Conflict, "already running"));

        var result = await Handler(context, storageApi, mintedId).Handle(
            new ExecuteImportJob(JobFor(deposit.MintedId), CallerPrincipal()), TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await using var verify = fixture.CreateNewPreservationContext();
        (await verify.ImportJobs.AnyAsync(j => j.Id == mintedId, TestContext.Current.CancellationToken)).Should().BeFalse();
    }
}
