using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
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

    // The margin a cached token is retired ahead of its real expiry, and also the fallback
    // lifetime used whenever expires_in can't be trusted at all.
    private static readonly TimeSpan CacheMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ConservativeCacheLifetime = TimeSpan.FromMinutes(5);
    // The old fixed lifetime this replaces - kept as an upper bound so a token endpoint reporting
    // an unusually long expires_in can't make a stale credential sit in cache longer than the
    // platform has ever cached one for.
    private static readonly TimeSpan MaximumCacheLifetime = TimeSpan.FromMinutes(56);

    private readonly MemoryCache memoryCache;
    private readonly IAccessTokenProviderOptions? options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<AccessTokenProvider> logger;

    // One shared mint per cache key: concurrent misses for the same key (startup, or every caller
    // racing the expiry boundary together) must not each send Entra a token request. The Lazy's
    // own thread-safety is what collapses N concurrent misses into one HTTP call; the dictionary
    // just publishes it under the key so every caller finds the same one.
    private readonly ConcurrentDictionary<string, Lazy<Task<(string Token, TimeSpan Lifetime)>>> inFlightMints = new();

    public AccessTokenProvider(ILogger<AccessTokenProvider> logger, IAccessTokenProviderOptions? options,
        IHttpClientFactory httpClientFactory, ISystemClock? clock = null)
    {
        this.options = options;
        this.httpClientFactory = httpClientFactory;
#pragma warning disable CS0618 // Clock/ISystemClock is obsolete in favour of TimeProvider, not yet available in the net8.0 Microsoft.Extensions.Caching.Memory shipped here.
        memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = clock ?? new RealSystemClock() });
#pragma warning restore CS0618
        this.logger = logger;
    }

    private sealed class RealSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public async Task<string?> GetAccessToken(CancellationToken cancellationToken = default)
    {
        // Exit if no options configured
        if (options == null)
        {
            logger.LogWarning("No options configured for AccessTokenProvider");
            return null;
        }

        // Only the credential triplet is required. ResourceUri is optional and must NOT gate
        // acquisition: a reflection-based all-properties null check here would turn a deploy
        // without the new key into a token-acquisition outage. Whitespace counts as absent -
        // a " " credential would otherwise be sent to Entra instead of failing here.
        if (string.IsNullOrWhiteSpace(options.TenantId)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            logger.LogWarning("AccessTokenProvider options are not configured correctly");
            return null;
        }

        // A whitespace-only ResourceUri is unset (it would build the invalid scope " /.default"),
        // and stray whitespace around a real one is config noise, not part of the resource.
        var resourceUri = string.IsNullOrWhiteSpace(options.ResourceUri)
            ? null
            : options.ResourceUri.Trim();

        // Keyed by the resource the token is FOR, now that ResourceUri makes this class
        // resource-generic. The cache is per-instance, so this is legibility, not collision
        // safety. TrimEnd matches the scope construction in GetBearerToken: with-slash and
        // without-slash spellings of the same resource are the same token.
        var key = "accessToken:" + (resourceUri is null
            ? $"api://{options.ClientId}"
            : resourceUri.TrimEnd('/'));
        if (memoryCache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        var tenantId = options.TenantId;
        var clientId = options.ClientId;
        var clientSecret = options.ClientSecret;

        var mint = inFlightMints.GetOrAdd(key, cacheKey => new Lazy<Task<(string, TimeSpan)>>(
            () => MintAndCache(cacheKey, tenantId, clientId, clientSecret, resourceUri)));

        // WaitAsync detaches only THIS caller from the wait: cancelling it must never cancel the
        // mint itself, which other callers may still be relying on (and which has its own bound,
        // the EntraTokenEndpoint client's 15-second timeout). A failed mint is never cached - see
        // MintAndCache - so the next call (from this caller or another) mints fresh.
        var (token, _) = await mint.Value.WaitAsync(cancellationToken);
        return token;
    }

    private async Task<(string Token, TimeSpan Lifetime)> MintAndCache(
        string key, string tenantId, string clientId, string clientSecret, string? resourceUri)
    {
        try
        {
            // CancellationToken.None: this task is shared by every caller that joined the same
            // in-flight mint, so no single caller's cancellation may abort it for the others.
            var (token, lifetime) = await GetBearerToken(tenantId, clientId, clientSecret, resourceUri,
                CancellationToken.None);
            memoryCache.Set(key, token, new MemoryCacheEntryOptions().SetAbsoluteExpiration(lifetime));
            return (token, lifetime);
        }
        finally
        {
            // Whether this mint succeeded or threw, it is no longer in flight: a later call must
            // either read the newly-cached value or start a fresh mint, never reuse this one.
            inFlightMints.TryRemove(key, out _);
        }
    }

    private async Task<(string Token, TimeSpan Lifetime)> GetBearerToken(string tenantId, string clientId,
        string clientSecret, string? resourceUri, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var collection = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", clientSecret)
        };

        string endpoint;
        if (!string.IsNullOrEmpty(resourceUri))
        {
            endpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            collection.Add(new("scope", $"{resourceUri.TrimEnd('/')}/.default"));
        }
        else
        {
            endpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";
            collection.Add(new("scope", $"api://{clientId}/.default"));
            collection.Add(new("resource", $"api://{clientId}"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new FormUrlEncodedContent(collection);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
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

        return (token, GetCacheLifetime(doc.RootElement));
    }

    /// <summary>
    /// expires_in minus the margin, bounded below by nothing (a token that expires inside the
    /// margin is still returned, just not cached for long) and above by the old fixed lifetime.
    /// Falls back to a conservative, briefly-cached lifetime when expires_in is missing, the wrong
    /// shape for both the v1.0 (string) and v2.0 (number) endpoints, or too short to be worth
    /// trusting at all.
    /// </summary>
    private TimeSpan GetCacheLifetime(JsonElement root)
    {
        if (root.TryGetProperty("expires_in", out var expiresInElement)
            && TryParseExpiresIn(expiresInElement, out var expiresInSeconds))
        {
            var expiresIn = TimeSpan.FromSeconds(expiresInSeconds);
            if (expiresIn > CacheMargin)
            {
                var lifetime = expiresIn - CacheMargin;
                return lifetime > MaximumCacheLifetime ? MaximumCacheLifetime : lifetime;
            }
        }

        // Property names only - never the body - matching the no-access_token diagnostic above.
        var properties = string.Join(", ", root.EnumerateObject().Select(property => property.Name));
        logger.LogWarning(
            "Token endpoint response had a missing, malformed, or too-short expires_in; caching for a " +
            "conservative {ConservativeMinutes} minutes (properties: {Properties})",
            ConservativeCacheLifetime.TotalMinutes, properties);
        return ConservativeCacheLifetime;
    }

    private static bool TryParseExpiresIn(JsonElement element, out long seconds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt64(out seconds):
                return true;
            // The v1.0 endpoint returns expires_in as a string of digits.
            case JsonValueKind.String when long.TryParse(element.GetString(), out seconds):
                return true;
            default:
                seconds = 0;
                return false;
        }
    }
}

public interface IAccessTokenProvider
{
    public Task<string?> GetAccessToken(CancellationToken cancellationToken = default);
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
        // AccessTokenProvider mints tokens through IHttpClientFactory, under this named client only
        // - HeaderPropagationMessageHandlerBuilderFilter keys off this exact name to keep platform
        // handlers off it. A short, explicit timeout replaces HttpClient's 100-second default: a
        // throttled Entra must fail this call fast rather than block every waiter for the maximum.
        services.AddHttpClient(AccessTokenProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
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
