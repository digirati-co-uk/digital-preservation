using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc.Filters;
using Storage.API.Fedora.Model;

namespace Storage.API.Web;

/// <summary>
/// Refuses, with a 400, any request whose repository-path route value is not a path under the
/// repository root (see <see cref="SafeRepositoryPath"/>), before the action runs. Registered globally so
/// that every controller taking a path - repository, content, import, OCFL - is covered without each
/// having to remember. <see cref="Converters.GetFedoraUri"/> applies the same rule again as a last line
/// of defence, but there it can only throw; here the caller gets told what was wrong.
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
                && !SafeRepositoryPath.IsUnderRoot(path, out var reason))
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
