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
    /// <summary>The named <see cref="IHttpClientFactory"/> client used for token requests.</summary>
    public const string HttpClientName = "EntraTokenEndpoint";

    private readonly MemoryCache memoryCache;
    private readonly IAccessTokenProviderOptions? options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<AccessTokenProvider> logger;


    public AccessTokenProvider(ILogger<AccessTokenProvider> logger, IAccessTokenProviderOptions? options,
        IHttpClientFactory httpClientFactory)
    {
        this.options = options;
        this.httpClientFactory = httpClientFactory;
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

        // Keyed by the resource the token is FOR, now that ResourceUri makes this class
        // resource-generic. The cache is per-instance, so this is legibility, not collision
        // safety. TrimEnd matches the scope construction in GetBearerToken: with-slash and
        // without-slash spellings of the same resource are the same token.
        var key = "accessToken:" + (string.IsNullOrEmpty(options.ResourceUri)
            ? $"api://{options.ClientId}"
            : options.ResourceUri.TrimEnd('/'));
        if (memoryCache.TryGetValue(key, out string? token))
        {
            return token;
        }
        // A failed mint THROWS to the caller and is never cached: a cached null would send
        // machine-to-machine calls out unauthenticated, silently, for the best part of an hour.
        token = await GetBearerToken(options.TenantId, options.ClientId, options.ClientSecret,
            options.ResourceUri);
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(56)) // assume token is valid for 1 hour
            .SetSlidingExpiration(TimeSpan.FromMinutes(55));
        memoryCache.Set(key, token, cacheEntryOptions);
        return token;
    }


    private async Task<string> GetBearerToken(string tenantId, string clientId, string clientSecret,
        string? resourceUri)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var collection = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", clientSecret)
        };

        HttpRequestMessage request;
        if (!string.IsNullOrEmpty(resourceUri))
        {
            request = new HttpRequestMessage(HttpMethod.Post,
                $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token");
            collection.Add(new("scope", $"{resourceUri.TrimEnd('/')}/.default"));
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Post,
                $"https://login.microsoftonline.com/{tenantId}/oauth2/token");
            collection.Add(new("scope", $"api://{clientId}/.default"));
            collection.Add(new("resource", $"api://{clientId}"));
        }

        var content = new FormUrlEncodedContent(collection);
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        // Not Dictionary<string, string>: the v2.0 endpoint returns expires_in as a JSON number.
        using var doc = JsonDocument.Parse(json);
        // The ValueKind check keeps a non-string access_token (a malformed body like
        // {"access_token": 12345}) on THIS controlled path: GetString() on a number would throw
        // .NET's generic InvalidOperationException before the diagnostic below is reached.
        if (!doc.RootElement.TryGetProperty("access_token", out var accessToken)
            || accessToken.ValueKind != JsonValueKind.String
            || accessToken.GetString() is not { Length: > 0 } token)
        {
            // Loud, immediate, and uncached - the old Dictionary indexer threw here too. Property
            // NAMES only: a token endpoint response body must never reach a log.
            var properties = string.Join(", ",
                doc.RootElement.EnumerateObject().Select(property => property.Name));
            logger.LogError("Token endpoint answered {StatusCode} without an access_token (properties: {Properties})",
                (int)response.StatusCode, properties);
            throw new InvalidOperationException(
                $"Token endpoint answered {(int)response.StatusCode} without an access_token (properties: {properties})");
        }
        return token;
    }

}

public interface IAccessTokenProvider
{
    public Task<string?> GetAccessToken();
}

public static class AccessTokenProviderX
{
    /// <summary>
    /// The one way to register <see cref="AccessTokenProvider"/>: the prepared options (each host
    /// builds its own - config section or secrets provider), the IHttpClientFactory it mints
    /// through, and the singleton itself. Hoisted so a change to this wiring cannot drift across
    /// the composition roots that need it.
    /// </summary>
    public static IServiceCollection AddAccessTokenProvider(this IServiceCollection services,
        IAccessTokenProviderOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpClient(); // AccessTokenProvider mints tokens through IHttpClientFactory
        services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();
        return services;
    }
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
