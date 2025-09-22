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
            apiKeyValid = false;
        }
        else
        {
            context.Request.Headers.TryGetValue(headerName, out var extractedApiKey);

            apiKeyValid = extractedApiKey == apiKey;
        }

        context.Items.Add(new(ApiContextObjectName, apiKeyValid));

        await next(context);
    }
}
