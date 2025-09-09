using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Pipeline.API.Middleware;

[AttributeUsage(AttributeTargets.Class)]
public class ApiKeyAttribute : Attribute, IAuthorizationFilter
{
    private const string ApiContextObjectName = "ApiKeyValid";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        //If item does not exist then middleware is not running.
        //This can be removed once feature flag is removed
        if (!context.HttpContext.Items.ContainsKey(ApiContextObjectName))
            return;

        context.HttpContext.Items.TryGetValue(ApiContextObjectName, out var hasValidApiKey);

        if (!Convert.ToBoolean(hasValidApiKey))
            context.Result = new ContentResult
            {
                StatusCode = 401,
                Content = "Invalid API Key"
            };
    }
}