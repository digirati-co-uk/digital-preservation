using System.Security.Claims;
using Microsoft.Identity.Web;

namespace DigitalPreservation.Core.Auth;

/// <summary>
/// The permission a caller must hold once <c>FeatureFlags:RequireCallerRole</c> is on (RFC-0001
/// Phase 4, issue #293): the <see cref="AppRole"/> app role for a machine caller, or the
/// <see cref="DelegatedScope"/> delegated scope for a human calling through the UI. Both are defined
/// on the API registration in Phase 0 (docs/rfc-0001-phase0-entra-admin.md steps 1.2 and 1.3).
/// </summary>
/// <remarks>
/// Role OR scope, never role alone: a delegated token carries <c>scp</c> and never <c>roles</c>, so a
/// role-only check would refuse every person using the UI (RFC-0001 section 5.3).
/// </remarks>
public static class CallerPermissions
{
    /// <summary>The app role (application permission) a machine caller is assigned.</summary>
    public const string AppRole = "Preservation.Call";

    /// <summary>The delegated scope a human's token carries when the UI calls on their behalf.</summary>
    public const string DelegatedScope = "access_as_user";

    /// <summary>
    /// True when the principal holds <see cref="AppRole"/> in its <c>roles</c> or
    /// <see cref="DelegatedScope"/> in its <c>scp</c>. Reads both the raw claim types and their
    /// .NET-mapped forms, as <see cref="ClaimsPrincipalX.IsHumanCaller"/> does, so the answer doesn't
    /// depend on whether inbound claim mapping is on.
    /// </summary>
    public static bool HasCallerPermission(this ClaimsPrincipal? principal)
    {
        if (principal == null)
        {
            return false;
        }

        var hasRole = principal.Claims.Any(c =>
            (c.Type == ClaimConstants.Roles || c.Type == ClaimTypes.Role)
            && c.Value == AppRole);
        if (hasRole)
        {
            return true;
        }

        // scp is a single space-separated list; a mapped token may carry it under the long name.
        return principal.Claims
            .Where(c => c.Type == ClaimConstants.Scp || c.Type == ClaimConstants.Scope)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(DelegatedScope, StringComparer.Ordinal);
    }
}
