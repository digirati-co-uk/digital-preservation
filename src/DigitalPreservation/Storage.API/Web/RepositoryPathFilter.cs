using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc.Filters;
using Storage.API.Fedora.Model;

namespace Storage.API.Web;

/// <summary>
/// Refuses, with a 400, any request whose repository-path <em>route value</em> (<c>path</c> or
/// <c>archivalGroupPathUnderRoot</c>) is not a path under the repository root (see
/// <see cref="SafeRepositoryPath"/>), before the action runs. Registered globally, so the repository,
/// content, import and OCFL controllers are covered without each having to remember - but only for
/// those two route-value names. Paths that arrive in a request body (an Import Job's or Export's
/// Archival Group and resource ids) never pass through here; the import and export queue handlers
/// validate those themselves. <see cref="Converters.GetFedoraUri"/> applies the rule once more as the
/// last line of defence, where it can only throw.
/// </summary>
public class RepositoryPathFilter : IActionFilter
{
    private static readonly string[] PathRouteValues = ["path", "archivalGroupPathUnderRoot"];

    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var name in PathRouteValues)
        {
            if (context.RouteData.Values.TryGetValue(name, out var value)
                && value is string path
                && !SafeRepositoryPath.IsRepositoryPath(path, out var reason))
            {
                context.Result = ControllerX.GetProblemObjectResult(
                    Result.Fail(ErrorCodes.BadRequest, $"'{path}' is not a path under the repository root: {reason}."));
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
