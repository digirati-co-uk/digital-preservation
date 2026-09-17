using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace DigitalPreservation.Core.Web.Headers;

/// <summary>
/// Used to get Bearer token from Azure AD for use in downstream API calls.
/// This will only be instantiated if added via DI in startup.
/// When <see cref="IAccessTokenProviderOptions.ResourceUri"/> is configured the token is requested
/// from the v2.0 endpoint for that resource (<c>scope = {ResourceUri}/.default</c>), so the minted
/// token's <c>aud</c> is the target API (RFC-0001 Phase 2). When it is not configured, behaviour is
/// unchanged: a v1.0 self-token for the caller's own registration.
/// </summary>
public class AccessTokenProvider : IAccessTokenProvider
{
    private readonly MemoryCache memoryCache;
    private readonly IAccessTokenProviderOptions? options;
    private readonly HttpMessageHandler? httpMessageHandler;
    private readonly string key = "storageApiAccessToken";
    private readonly ILogger<AccessTokenProvider> logger;


    public AccessTokenProvider(ILogger<AccessTokenProvider> logger, IAccessTokenProviderOptions? options,
        HttpMessageHandler? httpMessageHandler = null)
    {
        this.options = options;
        this.httpMessageHandler = httpMessageHandler;
        memoryCache = new MemoryCache(new MemoryCacheOptions());
        this.logger = logger;
    }

    public async Task<string?> GetAccessToken()
    {
        // Exit if no options configured
        if (options == null)
        {
            logger.LogWarning("No options configured for AccessTokenProvider");
            return null;
        }

        // Only the credential triplet is required. ResourceUri is optional and must NOT gate
        // acquisition: a reflection-based all-properties null check here would turn a deploy
        // without the new key into a token-acquisition outage.
        if (string.IsNullOrEmpty(options.TenantId)
            || string.IsNullOrEmpty(options.ClientId)
            || string.IsNullOrEmpty(options.ClientSecret))
        {
            logger.LogWarning("AccessTokenProvider options are not configured correctly");
            return null;
        }

        if (memoryCache.TryGetValue(key, out string? token))
        {
            return token;
        }
        token = await GetBearerToken();
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(56)) // assume token is valid for 1 hour
            .SetSlidingExpiration(TimeSpan.FromMinutes(55));
        memoryCache.Set(key, token, cacheEntryOptions);
        return token;
    }


    private async Task<string?> GetBearerToken()
    {
        using var client = httpMessageHandler == null
            ? new HttpClient()
            : new HttpClient(httpMessageHandler, disposeHandler: false);

        var collection = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", options!.ClientId!),
            new("client_secret", options.ClientSecret!)
        };

        HttpRequestMessage request;
        if (!string.IsNullOrEmpty(options.ResourceUri))
        {
            request = new HttpRequestMessage(HttpMethod.Post,
                $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token");
            collection.Add(new("scope", $"{options.ResourceUri.TrimEnd('/')}/.default"));
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Post,
                $"https://login.microsoftonline.com/{options.TenantId}/oauth2/token");
            collection.Add(new("scope", $"api://{options.ClientId}/.default"));
            collection.Add(new("resource", $"api://{options.ClientId}"));
        }

        var content = new FormUrlEncodedContent(collection);
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        // Not Dictionary<string, string>: the v2.0 endpoint returns expires_in as a JSON number.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("access_token", out var accessToken)
            ? accessToken.GetString()
            : null;
    }

}

public interface IAccessTokenProvider
{
    public Task<string?> GetAccessToken();
}


public class AccessTokenProviderOptions  : IAccessTokenProviderOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TenantId { get; set; }
    public string? ResourceUri { get; set; }
}

public interface IAccessTokenProviderOptions
{
    string? ClientId { get; set; }
    string? ClientSecret { get; set; }
    string? TenantId { get; set; }

    /// <summary>
    /// Optional App ID URI of the target API (e.g. <c>api://84c62880…</c>). When set, tokens are
    /// requested for this resource; when null or empty, the legacy self-token path is used.
    /// </summary>
    string? ResourceUri { get; set; }
}
