using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Preservation.API;

/// <summary>
/// Resource filter that returns 401 Unauthorized if the named FeatureFlags entry is false or absent.
/// Intended for [AllowAnonymous] endpoints that should be disabled in non-development environments.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireFeatureFlagAttribute(string flagName) : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>($"FeatureFlags:{flagName}"))
            context.Result = new UnauthorizedResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
