using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Preservation.API;

/// <summary>
/// Resource filter that returns 404 if the named FeatureFlags entry is false or absent. Intended
/// for [AllowAnonymous] endpoints that should be disabled in non-development environments - a
/// disabled feature isn't an authentication problem, and 401 on an anonymous endpoint is
/// misleading.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireFeatureFlagAttribute(string flagName) : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>($"FeatureFlags:{flagName}"))
            context.Result = ControllerX.ProblemResult(ErrorCodes.NotFound, null, "This endpoint is not enabled on this instance");
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
