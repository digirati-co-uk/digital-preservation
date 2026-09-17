using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        //Return if valid standard (human) user. IsHumanCaller is the shared predicate also used by
        //CallerResolver, so attribution, /whoami and bucket routing agree on who is human.
        if (context.HttpContext.User.IsHumanCaller())
        {
            return Task.CompletedTask;
        }

        //Machine (app-only) caller: derive the attribution name. Prefer the SIGNED token identity
        //(azp/appid resolved against the KnownClients allow-list); fall back to the self-asserted
        //X-Client-Identity header for callers not yet migrated (RFC-0001 Phase 0, dual mode).
        var machineName = ResolveMachineName(context);
        if (machineName != null)
        {
            var claims = new List<Claim> { new Claim("Name", machineName) };
            context.HttpContext.User.AddIdentity(new ClaimsIdentity(claims));

            return Task.CompletedTask;
        }

        //Must be un unauthorised
        context.Result = new UnauthorizedObjectResult("Unauthorized: No valid user name or machine header");
        return Task.CompletedTask;
    }

    //The filter is registered as a single instance in Program.cs, so this set lives for the process:
    //the fallback below warns once per unresolved appId (then logs at Debug), instead of a warning on
    //every request - the platform's own service-to-service calls all take the fallback path until
    //RFC-0001 Phase 2, which would otherwise be a warning per internal request. Per-caller migration
    //progress is observable via /whoami (source flips from header-fallback to token).
    private readonly ConcurrentDictionary<string, bool> warnedAppIds = new();

    private string? ResolveMachineName(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var clients = services.GetService<IClientDirectory>();

        var appId = context.HttpContext.User.FindFirst("azp")?.Value
                    ?? context.HttpContext.User.FindFirst("appid")?.Value;

        //1. Trustworthy: identity resolved from the signed token claim.
        if (clients != null && clients.TryResolve(appId, out var profile))
        {
            return profile.Name;
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
