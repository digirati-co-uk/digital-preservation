using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Mets;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Client;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// PatchDepositHandler's own GetAndValidateDeposit used to hand-roll its own copy of the
/// deposit-lock check, instead of the one rule Deposit.GetOtherLockOwner now defines for both the
/// model and the DB entity (issue #388 item 2.4). These pin that this handler goes through the
/// shared rule.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class PatchDepositHandlerTests(DatabaseFixture fixture)
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

    // Archived is set on the patch request so the handler skips ArchivalGroupRequestValidator
    // entirely and goes straight to the lock check this test is about.
    private static Deposit PatchRequest(string mintedId) => new()
    {
        Id = new Uri($"https://preservation.test/deposits/{mintedId}"),
        Archived = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict_Naming_The_Lock_Holder()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, LockHolder);
        var handler = new PatchDepositHandler(
            new NullLogger<PatchDepositHandler>(), context, A.Fake<IStorageApiClient>(), Mutator(), A.Fake<IMetsParser>());

        var result = await handler.Handle(
            new PatchDeposit(PatchRequest(deposit.MintedId), CallerPrincipal(OtherCaller)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain(LockHolder);
    }

    [Fact]
    public async Task Handle_Allows_The_Lock_Holders_Own_Call_To_Proceed()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, LockHolder);
        var handler = new PatchDepositHandler(
            new NullLogger<PatchDepositHandler>(), context, A.Fake<IStorageApiClient>(), Mutator(), A.Fake<IMetsParser>());

        var result = await handler.Handle(
            new PatchDeposit(PatchRequest(deposit.MintedId), CallerPrincipal(LockHolder)), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Allows_Any_Caller_When_The_Deposit_Is_Not_Locked()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, lockedBy: null);
        var handler = new PatchDepositHandler(
            new NullLogger<PatchDepositHandler>(), context, A.Fake<IStorageApiClient>(), Mutator(), A.Fake<IMetsParser>());

        var result = await handler.Handle(
            new PatchDeposit(PatchRequest(deposit.MintedId), CallerPrincipal(OtherCaller)), CancellationToken.None);

        result.Success.Should().BeTrue();
    }
}
