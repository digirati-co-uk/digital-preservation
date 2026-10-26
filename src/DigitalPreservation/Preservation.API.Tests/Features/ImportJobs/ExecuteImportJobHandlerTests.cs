using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using LeedsDlipServices.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Client;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.ImportJobs;

/// <summary>
/// ExecuteImportJobHandler used to hand-roll its own copy of the deposit-lock check, instead of the
/// one rule Deposit.GetOtherLockOwner now defines for both the model and the DB entity (issue #388
/// item 2.4). Diverged: the conflict message named the caller's own identity instead of the lock
/// holder's - the one piece of information the message exists to report - because the hand-rolled
/// check built its string from callerIdentity rather than depositEntity.LockedBy. These pin the fix.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class ExecuteImportJobHandlerTests(DatabaseFixture fixture)
{
    private const string LockHolder = "the-lock-holder";
    private const string OtherCaller = "someone-else";

    private static ClaimsPrincipal CallerPrincipal(string identity) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, identity)], "test"));

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));

    private async Task<DepositEntity> SeedDeposit(Preservation.API.Data.PreservationContext context, string? lockedBy)
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
            LockedBy = lockedBy
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return deposit;
    }

    private static ImportJob JobFor(string mintedId) => new()
    {
        Deposit = new Uri($"https://preservation.test/deposits/{mintedId}")
    };

    [Fact]
    public async Task Handle_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict_Naming_The_Lock_Holder()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, LockHolder);
        var handler = new ExecuteImportJobHandler(
            new NullLogger<ExecuteImportJobHandler>(), A.Fake<IStorageApiClient>(), context,
            A.Fake<IIdentityService>(), Mutator());

        var result = await handler.Handle(
            new ExecuteImportJob(JobFor(deposit.MintedId), CallerPrincipal(OtherCaller)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);

        // The bug this pins: the message used to name the caller (OtherCaller), not the lock
        // holder - useless to a caller trying to find out who actually holds the lock.
        result.ErrorMessage.Should().Contain(LockHolder);
        result.ErrorMessage.Should().NotContain(OtherCaller);
    }

    [Fact]
    public async Task Handle_Allows_The_Lock_Holders_Own_Call_To_Proceed_Past_The_Lock_Check()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, LockHolder);
        var identityService = A.Fake<IIdentityService>();
        A.CallTo(() => identityService.MintIdentity(A<string>._)).Returns("job-" + Guid.NewGuid());
        var handler = new ExecuteImportJobHandler(
            new NullLogger<ExecuteImportJobHandler>(), A.Fake<IStorageApiClient>(), context,
            identityService, Mutator());

        var result = await handler.Handle(
            new ExecuteImportJob(JobFor(deposit.MintedId), CallerPrincipal(LockHolder)), CancellationToken.None);

        // Whatever happens further down (there's no Fedora here to actually import into) is
        // irrelevant to this test - only that the lock check itself didn't refuse the call.
        result.ErrorCode.Should().NotBe(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Handle_Allows_Any_Caller_When_The_Deposit_Is_Not_Locked()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, lockedBy: null);
        var identityService = A.Fake<IIdentityService>();
        A.CallTo(() => identityService.MintIdentity(A<string>._)).Returns("job-" + Guid.NewGuid());
        var handler = new ExecuteImportJobHandler(
            new NullLogger<ExecuteImportJobHandler>(), A.Fake<IStorageApiClient>(), context,
            identityService, Mutator());

        var result = await handler.Handle(
            new ExecuteImportJob(JobFor(deposit.MintedId), CallerPrincipal(OtherCaller)), CancellationToken.None);

        result.ErrorCode.Should().NotBe(ErrorCodes.Conflict);
    }
}
