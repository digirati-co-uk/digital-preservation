using Microsoft.Extensions.Options;
using Pipeline.API.Config;

namespace Pipeline.API.Middleware;

public class ApiKeyMiddleware(IOptions<ApiKeyOptions> apiKeyOptions) : IMiddleware
{
    private readonly string? apiKey = apiKeyOptions.Value.ApiKey;
    private readonly string? headerName = apiKeyOptions.Value.ApiHeaderName;
    private const string ApiContextObjectName = "ApiKeyValid";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        bool apiKeyValid;

        if (string.IsNullOrEmpty(headerName))
        {
            context.Items.Add(new(ApiContextObjectName, false));
        }
        else
        {
            apiKeyValid = context.Request.Headers.TryGetValue(headerName, out var extractedApiKey);

            if (extractedApiKey != apiKey)
                apiKeyValid = false;

            context.Items.Add(new(ApiContextObjectName, apiKeyValid));
        }

        await next(context);
    }
}
