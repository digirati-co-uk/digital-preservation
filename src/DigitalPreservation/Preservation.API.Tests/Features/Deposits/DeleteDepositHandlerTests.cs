using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Repository.Common;
using DepositEntity = Preservation.API.Data.Entities.Deposit;

namespace Preservation.API.Tests.Features.Deposits;

/// <summary>
/// DeleteDepositHandler used to hand-roll its own copy of the deposit-lock check, instead of the one
/// rule Deposit.GetOtherLockOwner now defines for both the model and the DB entity (issue #388 item
/// 2.4). These pin that this handler goes through the shared rule: a lock held by someone else
/// refuses the delete, the lock holder's own call proceeds, and so does an unlocked deposit.
///
/// Files is deliberately non-null on every seeded deposit here: a null Files short-circuits the
/// handler's own success return (an unrelated, pre-existing quirk the issue notes separately under
/// "Found along the way", not something item 2.4 touches), and these tests exist only to pin the
/// lock rule, not that quirk.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class DeleteDepositHandlerTests(DatabaseFixture fixture)
{
    private const string LockHolder = "the-lock-holder";
    private const string OtherCaller = "someone-else";

    private static ClaimsPrincipal CallerPrincipal(string identity) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, identity)], "test"));

    private static IStorage SucceedingStorage()
    {
        var storage = A.Fake<IStorage>();
        A.CallTo(() => storage.EmptyStorageLocation(A<Uri>._, A<CancellationToken>._))
            .Returns(Task.FromResult(Result.OkNotNull(new BulkDeleteResult())));
        return storage;
    }

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
            LockedBy = lockedBy,
            Files = new Uri("s3://bucket/deposits/" + Guid.NewGuid())
        };
        context.Deposits.Add(deposit);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return deposit;
    }

    [Fact]
    public async Task Handle_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict_Naming_The_Lock_Holder()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, LockHolder);
        var handler = new DeleteDepositHandler(
            new NullLogger<DeleteDepositHandler>(), context, SucceedingStorage());

        var result = await handler.Handle(
            new DeleteDeposit(deposit.MintedId, CallerPrincipal(OtherCaller)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain(LockHolder);
    }

    [Fact]
    public async Task Handle_Allows_The_Lock_Holders_Own_Call_To_Proceed()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, LockHolder);
        var handler = new DeleteDepositHandler(
            new NullLogger<DeleteDepositHandler>(), context, SucceedingStorage());

        var result = await handler.Handle(
            new DeleteDeposit(deposit.MintedId, CallerPrincipal(LockHolder)), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Allows_Any_Caller_When_The_Deposit_Is_Not_Locked()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var deposit = await SeedDeposit(context, lockedBy: null);
        var handler = new DeleteDepositHandler(
            new NullLogger<DeleteDepositHandler>(), context, SucceedingStorage());

        var result = await handler.Handle(
            new DeleteDeposit(deposit.MintedId, CallerPrincipal(OtherCaller)), CancellationToken.None);

        result.Success.Should().BeTrue();
    }
}
