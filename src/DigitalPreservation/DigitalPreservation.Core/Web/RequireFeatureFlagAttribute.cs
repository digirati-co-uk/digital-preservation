using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Web;

/// <summary>
/// Resource filter that returns 404 if the named FeatureFlags entry is false or absent. Intended
/// for [AllowAnonymous] endpoints that should be disabled in non-development environments - a
/// disabled feature isn't an authentication problem, and 401 on an anonymous endpoint is
/// misleading. Existed as an identical copy in both Preservation.API and Pipeline.API (issue #388
/// item 3.5); both already depend on this project, the same as the other cross-cutting filters
/// here such as AuthFilterIdentifier.
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
