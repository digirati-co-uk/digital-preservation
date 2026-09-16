using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc.Filters;
using Storage.API.Fedora.Model;

namespace Storage.API.Web;

/// <summary>
/// Refuses, with a 400, any request carrying a <em>route value</em> that is not a path under the
/// repository root (see <see cref="SafeRepositoryPath"/>), before the action runs. Registered
/// globally and applied to every route value regardless of name, so a controller cannot opt out by
/// naming its parameter differently. Paths that arrive in a request body (an Import Job's or
/// Export's Archival Group and resource ids) never pass through here; the import and export
/// handlers validate those themselves. <see cref="Converters.GetFedoraUri"/> applies the rule once more as the
/// last line of defence, where it can only throw.
/// </summary>
public class RepositoryPathFilter : IActionFilter
{
    // Routing's own values, never caller data.
    private static readonly HashSet<string> RoutingValues =
        new(StringComparer.OrdinalIgnoreCase) { "controller", "action", "area", "page", "handler" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        // Every route value, whatever its name: a future {*resourcePath} is covered the day it is
        // written. Nothing legitimate is lost - every other route value is a minted identifier or a
        // slug, which the rule accepts.
        foreach (var (name, value) in context.RouteData.Values)
        {
            if (RoutingValues.Contains(name) || value is not string candidate)
            {
                continue;
            }
            if (!SafeRepositoryPath.IsRepositoryPath(candidate, out var reason))
            {
                context.Result = ControllerX.GetProblemObjectResult(
                    Result.Fail(ErrorCodes.BadRequest, $"'{candidate}' is not a path under the repository root: {reason}."));
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
