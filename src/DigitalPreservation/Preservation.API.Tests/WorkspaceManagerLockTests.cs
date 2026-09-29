using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using FakeItEasy;
using MediatR;

namespace Preservation.API.Tests;

/// <summary>
/// A deposit locked by another caller must be refused the same way from every WorkspaceManager
/// entry point (issue #258): these four checks used to answer 401 Unauthorized while the
/// Preservation API's own lock checks (PatchDeposit, DeleteDeposit, LockDeposit, and others)
/// already answered 409 Conflict for the identical condition. 401 is the wrong code here - the
/// caller is authenticated and authorised, and no credential would help; a client that treats 401
/// as "my token is bad" would refresh it and retry forever.
/// </summary>
public class WorkspaceManagerLockTests
{
    private const string LockHolder = "the-lock-holder";
    private const string OtherCaller = "someone-else";

    private static WorkspaceManager CreateManager() =>
        new(
            new Deposit { LockedBy = new Uri("https://preservation.test/agents/" + LockHolder) },
            A.Fake<IMediator>(),
            A.Fake<IMetsParser>());

    [Fact]
    public async Task CreateFolder_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict()
    {
        var manager = CreateManager();

        var result = await manager.CreateFolder("new-folder", null, false, OtherCaller);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task DeleteItems_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict()
    {
        var manager = CreateManager();

        var result = await manager.DeleteItems(new DeleteSelection(), OtherCaller);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task AddItemsToMets_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict()
    {
        var manager = CreateManager();

        var result = await manager.AddItemsToMets([], OtherCaller);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task UploadSingleSmallFile_Refuses_A_Deposit_Locked_By_Someone_Else_With_Conflict()
    {
        var manager = CreateManager();

        var result = await manager.UploadSingleSmallFile(
            Stream.Null, 0, "file.txt", "checksum", "file.txt", "text/plain", null, OtherCaller);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task The_Lock_Holders_Own_Call_Is_Not_Refused_As_A_Conflict()
    {
        // A control: the lock check must not fire for the caller who actually holds the lock.
        // Whatever the fakes make of the call afterwards is irrelevant - only that it isn't Conflict.
        var manager = CreateManager();

        var result = await manager.AddItemsToMets([], LockHolder);

        result.ErrorCode.Should().NotBe(ErrorCodes.Conflict);
    }
}
