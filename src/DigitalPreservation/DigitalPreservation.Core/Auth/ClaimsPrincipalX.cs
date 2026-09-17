using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Identity.Web;

namespace DigitalPreservation.Core.Auth;

public static class ClaimsPrincipalX
{
    private const string KeyValue = "upn"; // unique-name also an option

    public static string GetCallerIdentity(this ClaimsPrincipal? principal, bool allowAnonymous = false) =>
        principal?.GetDisplayName() ?? "dlipdev";

    public static string GetCallerIdentity(HttpContext? context) =>
        context == null ? "unknown" : GetCallerIdentity(context.Request);

    public static string GetCallerIdentity(HttpRequest? request)
    {
        if (request == null)
         return "unknown";
        
        var token = request.Headers["Authorization"].ToString();
        return GetCallerIdentity(token);
    }

    public static string GetCallerIdentity(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "unknown";

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token.Replace("Bearer ", ""));
        jwtToken.Payload.TryGetValue(KeyValue, out var upn);
        return upn?.ToString()?.Split("@")[0] ?? "unknown";
    }

    /// <summary>
    /// True when the principal carries a real user-identifying claim from a delegated (human) token:
    /// <c>preferred_username</c> (v2.0 tokens) or <c>upn</c>/<c>unique_name</c> (v1.0 tokens, in raw
    /// or .NET-mapped form). Deliberately does NOT consult display-name claims ("name"):
    /// <see cref="AuthFilterIdentifier"/> injects a "Name" claim for machine callers, so name-shaped
    /// claims cannot distinguish human from machine. This is the single human-vs-machine predicate
    /// shared by <see cref="AuthFilterIdentifier"/> and <see cref="CallerResolver"/>, so attribution,
    /// <c>/whoami</c> and deposit-bucket routing always agree (RFC-0001 §8 Q7; the <c>idtyp</c>
    /// claim is the eventual discriminator once all callers are guaranteed to carry it).
    /// </summary>
    public static bool IsHumanCaller(this ClaimsPrincipal? principal) =>
        principal != null &&
        (HasClaimValue(principal, ClaimConstants.PreferredUserName)
         || HasClaimValue(principal, ClaimTypes.Upn) || HasClaimValue(principal, "upn")
         || HasClaimValue(principal, ClaimTypes.Name) || HasClaimValue(principal, "unique_name"));

    private static bool HasClaimValue(ClaimsPrincipal principal, string claimType) =>
        !string.IsNullOrEmpty(principal.FindFirst(claimType)?.Value);
}
