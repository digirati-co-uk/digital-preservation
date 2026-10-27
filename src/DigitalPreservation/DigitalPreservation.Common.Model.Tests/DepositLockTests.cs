using DigitalPreservation.Common.Model.PreservationApi;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// Deposit.GetOtherLockOwner is now the one deposit-lock rule both the model and the Preservation
/// API's DB entity use (issue #388 item 2.4), instead of three hand-rolled copies that had drifted:
/// a wrong identity in one message, inconsistent null-caller handling, and three different
/// messages. These pin the rule itself, including the deliberate null/empty-caller bypass that
/// GetDepositBase's post-export reconciliation relies on to proceed regardless of who, if anyone,
/// holds the lock.
/// </summary>
public class DepositLockTests
{
    private const string LockHolder = "the-lock-holder";
    private const string OtherCaller = "someone-else";

    [Fact]
    public void A_Lock_Held_By_Someone_Else_Is_Reported()
    {
        Deposit.GetOtherLockOwner(LockHolder, OtherCaller).Should().Be(LockHolder);
    }

    [Fact]
    public void The_Lock_Holders_Own_Call_Is_Not_Reported()
    {
        Deposit.GetOtherLockOwner(LockHolder, LockHolder).Should().BeNull();
    }

    [Fact]
    public void An_Unlocked_Deposit_Reports_No_Owner()
    {
        Deposit.GetOtherLockOwner(null, OtherCaller).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_Null_Or_Empty_Caller_Bypasses_The_Lock(string? callerIdentity)
    {
        // Deliberate, not an oversight: a system-triggered action (not a user edit) passes no
        // caller identity to say "proceed regardless of who holds the lock" - see
        // Preservation.API's GetDepositBase, which depends on exactly this for its post-export
        // folder reconciliation.
        Deposit.GetOtherLockOwner(LockHolder, callerIdentity).Should().BeNull();
    }

    [Fact]
    public void The_Instance_Overload_Reads_The_Lock_From_The_Parsed_LockedBy_Uri()
    {
        var deposit = new Deposit { LockedBy = new Uri($"https://preservation.test/agents/{LockHolder}") };

        deposit.GetOtherLockOwner(OtherCaller).Should().Be(LockHolder);
        deposit.GetOtherLockOwner(LockHolder).Should().BeNull();
    }
}
