using System.Security.Claims;
using Microsoft.Identity.Web;

namespace DigitalPreservation.Core.Auth;

/// <summary>
/// Resolves the full calling identity for the <c>/whoami</c> endpoint (RFC-0001 §9.1). Pure — takes
/// the claims, the (self-asserted) <c>X-Client-Identity</c> header value, the <see cref="IClientDirectory"/>
/// allow-list and the API's default working bucket, and returns what the API would attribute the call to.
/// Mirrors the dual-mode logic in <see cref="AuthFilterIdentifier"/> (token-resolved preferred, header fallback).
/// </summary>
public static class CallerResolver
{
    public const string SourceUser = "user";
    public const string SourceToken = "token";
    public const string SourceHeaderFallback = "header-fallback";
    public const string SourceUnknown = "unknown";

    public static WhoAmIResult Resolve(
        ClaimsPrincipal user, string? clientIdentityHeader, IClientDirectory? clients, string defaultBucket)
    {
        var appId = user.FindFirst("azp")?.Value ?? user.FindFirst("appid")?.Value;

        // 1. Human (delegated) caller — identified by a real user claim, not the injected machine name.
        var preferredUsername = user.FindFirstValue("preferred_username");
        if (!string.IsNullOrEmpty(preferredUsername))
        {
            return new WhoAmIResult
            {
                Name = user.GetDisplayName() ?? preferredUsername,
                Source = SourceUser,
                AppId = appId,
                DepositBucket = defaultBucket
            };
        }

        // 2. Machine caller resolved from the signed token (trustworthy).
        if (clients != null && clients.TryResolve(appId, out var profile))
        {
            return new WhoAmIResult
            {
                Name = profile.Name,
                Source = SourceToken,
                AppId = appId,
                DepositBucket = profile.DepositBucket ?? defaultBucket
            };
        }

        // 3. Machine caller falling back to the self-asserted header (RFC-0001 Phase 0 dual mode).
        if (!string.IsNullOrEmpty(clientIdentityHeader))
        {
            return new WhoAmIResult
            {
                Name = clientIdentityHeader,
                Source = SourceHeaderFallback,
                AppId = appId,
                DepositBucket = defaultBucket
            };
        }

        // 4. Unknown caller.
        return new WhoAmIResult
        {
            Name = "unknown",
            Source = SourceUnknown,
            AppId = appId,
            DepositBucket = defaultBucket
        };
    }
}
