using System.Security.Cryptography;
using System.Text;
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

        if (string.IsNullOrEmpty(headerName) || apiKey is null)
        {
            apiKeyValid = false;
        }
        else
        {
            context.Request.Headers.TryGetValue(headerName, out var extractedApiKey);

            // string/StringValues == short-circuits at the first differing character, so a naive
            // comparison here leaks how much of the key prefix a caller got right via response
            // timing - exactly what a low-jitter network (this API is only reachable from inside the
            // VPC) makes practical to exploit. Comparing in constant time closes that.
            apiKeyValid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(extractedApiKey.ToString()),
                Encoding.UTF8.GetBytes(apiKey));
        }


        context.Items.Add(new(ApiContextObjectName, apiKeyValid));

        await next(context);
    }
}
