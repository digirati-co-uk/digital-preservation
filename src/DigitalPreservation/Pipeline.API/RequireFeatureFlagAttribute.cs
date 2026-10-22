using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Pipeline.API;

/// <summary>
/// Resource filter that returns 404 if the named FeatureFlags entry is false or absent. Mirrors
/// Preservation.API.RequireFeatureFlagAttribute - kept as a separate copy rather than a shared
/// dependency, the same call already made for the small pieces duplicated between these two
/// services (e.g. PipelineJobMessage).
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
