using Microsoft.Extensions.Options;
using Pipeline.API.Config;

namespace Pipeline.API.Middleware;

public class ApiKeyMiddleware(IOptions<ApiKeyOptions> apiKeyOptions) : IMiddleware
{
    private readonly string _apiKey = apiKeyOptions.Value.ApiKey;
    private readonly string _headerName = apiKeyOptions.Value.ApiHeaderName;
    private const string ApiContextObjectName = "ApiKeyValid";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var apiKeyValid = context.Request.Headers.TryGetValue(_headerName, out var extractedApiKey);

        if (extractedApiKey != _apiKey)
            apiKeyValid = false;

        context.Items.Add(new(ApiContextObjectName, apiKeyValid));

        await next(context);
    }
}
