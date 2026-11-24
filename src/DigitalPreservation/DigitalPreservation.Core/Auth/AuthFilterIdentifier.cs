using System.Collections.Concurrent;
using DigitalPreservation.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using System.Security.Claims;

namespace DigitalPreservation.Core.Auth;

public sealed class AuthFilterIdentifier : IAsyncAuthorizationFilter
{
    //This used by PropagateCorrelationIdHandler
    public const string MachineHeaderName = "X-Client-Identity";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        //Controller allows Anonymous so ignore this filter
        if (context.ActionDescriptor.EndpointMetadata
            .Any(em => em.GetType() == typeof(AllowAnonymousAttribute)))
        {
            return Task.CompletedTask;
        }

        // RFC-0001 Phase 4 (issue #293): both enforcement steps are configuration, off by default, so
        // the same build serves an environment that has finished the ladder and one that hasn't.
        // Read per request. In the deployed environments the flags are environment variables, which
        // are read once at startup, so changing one there means new tasks, not a new build.
        var flags = context.HttpContext.RequestServices.GetService<IOptionsMonitor<FeatureFlagOptions>>()?.CurrentValue
                    ?? new FeatureFlagOptions();
        var user = context.HttpContext.User;

        // /whoami is how a caller finds out why it is refused, so the two flags don't refuse it there.
        // Everything else about the endpoint is unchanged: with both flags off it behaves as before.
        var diagnostic = context.ActionDescriptor.EndpointMetadata
            .Any(em => em is ExemptFromCallerEnforcementAttribute);

        //Return if valid standard (human) user. IsHumanCaller is the shared predicate also used by
        //CallerResolver, so attribution, /whoami and bucket routing agree on who is human.
        if (user.IsHumanCaller())
        {
            if (flags.RequireCallerRole && !user.HasCallerPermission() && !diagnostic)
            {
                // Every person comes through the UI's app ID, so the person is named as well.
                context.Result = Refuse(context, AppIdOf(user),
                    $"Forbidden: the token does not carry the '{CallerPermissions.DelegatedScope}' scope",
                    user.GetObjectId() ?? user.GetDisplayName());
            }
            return Task.CompletedTask;
        }

        //Machine (app-only) caller: derive the attribution name. Prefer the SIGNED token identity
        //(azp/appid resolved against the KnownClients allow-list); fall back to the self-asserted
        //X-Client-Identity header for callers not yet migrated (RFC-0001 Phase 0, dual mode), unless
        //RequireKnownCaller has switched dual mode off.
        var machineName = ResolveMachineName(context, flags.RequireKnownCaller);
        if (machineName == null)
        {
            var appId = AppIdOf(user);
            if (flags.RequireKnownCaller && appId != null && diagnostic)
            {
                // Let it through with no name: /whoami reports it as 'unknown'.
                return Task.CompletedTask;
            }
            context.Result = flags.RequireKnownCaller && appId != null
                // Authenticated, but not a caller this API knows: a refusal, not a missing credential.
                ? Refuse(context, appId, $"Forbidden: caller {appId} is not a known client")
                : new UnauthorizedObjectResult("Unauthorized: No valid user name or machine header");
            return Task.CompletedTask;
        }

        if (flags.RequireCallerRole && !user.HasCallerPermission() && !diagnostic)
        {
            context.Result = Refuse(context, AppIdOf(user),
                $"Forbidden: the token does not carry the '{CallerPermissions.AppRole}' app role");
            return Task.CompletedTask;
        }

        var claims = new List<Claim> { new Claim("Name", machineName) };
        user.AddIdentity(new ClaimsIdentity(claims));
        return Task.CompletedTask;
    }

    //The filter is registered as a single instance in Program.cs, so these sets live for the process:
    //the fallback below warns once per unresolved appId (then logs at Debug), instead of a warning on
    //every request - the platform's own service-to-service calls all take the fallback path until
    //RFC-0001 Phase 2, which would otherwise be a warning per internal request. Per-caller migration
    //progress is observable via /whoami (source flips from header-fallback to token). Refusals under
    //the Phase 4 flags follow the same once-then-Debug rule, so a misconfigured caller can't flood the log.
    private readonly ConcurrentDictionary<string, bool> warnedAppIds = new();
    private readonly ConcurrentDictionary<string, bool> refusedAppIds = new();

    private static string? AppIdOf(ClaimsPrincipal user) =>
        user.FindFirst("azp")?.Value ?? user.FindFirst("appid")?.Value;

    /// <param name="person">
    /// For a person, their object ID (or name): part of the once-then-Debug key and of the message,
    /// so each refused person gets a Warning and the log says who it was. Null for a machine.
    /// </param>
    private ObjectResult Refuse(AuthorizationFilterContext context, string? appId, string reason,
        string? person = null)
    {
        var logger = context.HttpContext.RequestServices.GetService<ILogger<AuthFilterIdentifier>>();
        var caller = person == null ? appId ?? "(no azp)" : $"{appId ?? "(no azp)"} (user {person})";
        if (refusedAppIds.TryAdd($"{caller}|{reason}", true))
        {
            logger?.LogWarning("Refused caller {Caller}: {Reason}. Further refusals of this caller for this " +
                               "reason log at Debug.", caller, reason);
        }
        else
        {
            logger?.LogDebug("Refused caller {Caller}: {Reason}", caller, reason);
        }
        return new ObjectResult(reason) { StatusCode = StatusCodes.Status403Forbidden };
    }

    private string? ResolveMachineName(AuthorizationFilterContext context, bool requireKnownCaller)
    {
        var services = context.HttpContext.RequestServices;
        var clients = services.GetService<IClientDirectory>();

        var appId = AppIdOf(context.HttpContext.User);

        //1. Trustworthy: identity resolved from the signed token claim.
        if (clients != null && clients.TryResolve(appId, out var profile))
        {
            return profile.Name;
        }

        //Phase 4: the self-asserted header no longer identifies anyone (issue #293).
        if (requireKnownCaller)
        {
            return null;
        }

        //2. Fallback: self-asserted, unauthenticated header (current behaviour, to be removed in Phase 4).
        if (context.HttpContext.Request.Headers.TryGetValue(MachineHeaderName, out var header))
        {
            var headerValue = header.FirstOrDefault() ?? string.Empty;
            var logger = services.GetService<ILogger<AuthFilterIdentifier>>();
            if (warnedAppIds.TryAdd(appId ?? "(no azp)", true))
            {
                logger?.LogWarning(
                    "Caller {AppId} not resolved from KnownClients; falling back to self-asserted "
                    + "X-Client-Identity '{ClientIdentity}'. Further requests from this appId log at Debug.",
                    appId ?? "(no azp)", headerValue);
            }
            else
            {
                logger?.LogDebug(
                    "Caller {AppId} not resolved from KnownClients; falling back to self-asserted "
                    + "X-Client-Identity '{ClientIdentity}'", appId ?? "(no azp)", headerValue);
            }
            return headerValue;
        }

        return null;
    }
}
