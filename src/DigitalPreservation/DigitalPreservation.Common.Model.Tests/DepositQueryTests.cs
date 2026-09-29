using DigitalPreservation.Common.Model.PreservationApi;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// Archived and Active must count as terms of their own (issue #263): before this, a query
/// carrying only one of them took GetDepositsHandler's no-terms branch, which silently applies
/// its own hard-coded active-only filter instead of the caller's.
/// </summary>
public class DepositQueryTests
{
    [Fact]
    public void Query_With_Only_Archived_Is_Not_NoTerms()
    {
        new DepositQuery { Archived = true }.NoTerms().Should().BeFalse();
    }

    [Fact]
    public void Query_With_Only_Active_Is_Not_NoTerms()
    {
        new DepositQuery { Active = false }.NoTerms().Should().BeFalse();
    }

    [Fact]
    public void Empty_Query_Is_NoTerms()
    {
        new DepositQuery().NoTerms().Should().BeTrue();
    }
}
