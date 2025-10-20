using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DigitalPreservation.Core.Web.Headers;

/// <summary>
/// Used to get Bearer token from Azure AD for use in downstream API calls.
/// This will only be instantiated if added via DI in startup.
/// </summary>
public class AccessTokenProvider : IAccessTokenProvider
{
    private readonly MemoryCache memoryCache;
    private readonly IAccessTokenProviderOptions? options;
    private readonly string key = "storageApiAccessToken";
    private readonly ILogger<AccessTokenProvider> logger;


    public AccessTokenProvider(ILogger<AccessTokenProvider> logger, IAccessTokenProviderOptions? options)
    {
        this.options = options;
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

        var nullCheck = options.GetType()
            .GetProperties() //get all properties on object
            .Select(pi => pi.GetValue(options)) //get value for the property
            .Any(value => value == null);

        if (nullCheck)
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
        
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, 
            $"https://login.microsoftonline.com/{options?.TenantId}/oauth2/token");
        var collection = new List<KeyValuePair<string, string>>();
        collection.Add(new("grant_type", "client_credentials"));
        if (options is { ClientId: not null })
        {
            collection.Add(new("client_id", options.ClientId));
            if (options.ClientSecret != null) collection.Add(new("client_secret", options.ClientSecret));
            collection.Add(new("scope", $"api://{options.ClientId}/.default"));
            collection.Add(new("resource", $"api://{options.ClientId}"));
        }

        var content = new FormUrlEncodedContent(collection);
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

       return obj?["access_token"];
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
}

public interface IAccessTokenProviderOptions
{
    string? ClientId { get; set; }
    string? ClientSecret { get; set; }
    string? TenantId { get; set; }
}
