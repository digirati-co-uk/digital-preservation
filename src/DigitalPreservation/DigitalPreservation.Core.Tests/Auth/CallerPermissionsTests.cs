using System.Security.Claims;
using DigitalPreservation.Core.Auth;

namespace DigitalPreservation.Core.Tests.Auth;

/// <summary>
/// The permission FeatureFlags:RequireCallerRole demands (RFC-0001 Phase 4, issue #293): the
/// Preservation.Call app role, or the access_as_user delegated scope.
/// </summary>
public class CallerPermissionsTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Theory]
    [InlineData("roles")]
    [InlineData(ClaimTypes.Role)]
    public void AppRole_UnderAnyClaimSpelling_Counts(string claimType)
    {
        Principal(("azp", "x"), (claimType, "Preservation.Call")).HasCallerPermission().Should().BeTrue();
    }

    [Theory]
    [InlineData("scp", "access_as_user")]
    [InlineData("scp", "User.Read access_as_user offline_access")]
    [InlineData("http://schemas.microsoft.com/identity/claims/scope", "access_as_user")]
    public void DelegatedScope_InTheSpaceSeparatedList_Counts(string claimType, string value)
    {
        Principal(("preferred_username", "alice@leeds.ac.uk"), (claimType, value)).HasCallerPermission().Should().BeTrue();
    }

    [Fact]
    public void SeveralRoleClaims_AreAllConsidered()
    {
        Principal(("roles", "Something.Else"), ("roles", "Preservation.Call")).HasCallerPermission().Should().BeTrue();
    }

    [Theory]
    [InlineData("roles", "Preservation.Read")]
    [InlineData("roles", "preservation.call")]
    [InlineData("scp", "User.Read")]
    [InlineData("scp", "access_as_user_extra")]
    [InlineData("roles", "access_as_user")]
    [InlineData("scp", "Preservation.Call")]
    public void NearMisses_DoNotCount(string claimType, string value)
    {
        // Exact, case-sensitive values, each in its own claim: a role named like the scope, or a
        // scope named like the role, is not the permission.
        Principal((claimType, value)).HasCallerPermission().Should().BeFalse();
    }

    [Fact]
    public void NoClaims_OrNoPrincipal_DoNotCount()
    {
        Principal().HasCallerPermission().Should().BeFalse();
        ((ClaimsPrincipal?)null).HasCallerPermission().Should().BeFalse();
    }

    // ---- /whoami under the flags ----

    private const string Unknown = "99999999-9999-9999-9999-999999999999";

    [Fact]
    public void RequireKnownCaller_UnknownMachineWithHeader_ResolvesToUnknown_NotTheHeader()
    {
        var result = CallerResolver.Resolve(Principal(("azp", Unknown)), "legacy-caller",
            new ClientDirectory(new Dictionary<string, ClientProfile>()), "dev-deposits", requireKnownCaller: true);

        result.Source.Should().Be(CallerResolver.SourceUnknown);
        result.Name.Should().Be("unknown");
    }

    [Fact]
    public void WhoAmI_ReportsWhetherTheCallerHasThePermission()
    {
        var directory = new ClientDirectory(new Dictionary<string, ClientProfile>());

        CallerResolver.Resolve(Principal(("azp", Unknown), ("roles", "Preservation.Call")), "x", directory, "b")
            .HasCallerPermission.Should().BeTrue();
        CallerResolver.Resolve(Principal(("azp", Unknown)), "x", directory, "b")
            .HasCallerPermission.Should().BeFalse();
        CallerResolver.Resolve(Principal(("preferred_username", "a@b"), ("scp", "access_as_user")), null, directory, "b")
            .HasCallerPermission.Should().BeTrue();
    }
}
