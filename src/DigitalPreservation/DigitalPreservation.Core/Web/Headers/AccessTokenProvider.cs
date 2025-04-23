using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace DigitalPreservation.Core.Web.Headers;

public  class AccessTokenProvider
{
    private readonly MemoryCache memoryCache;
    private readonly IAccessTokenProviderOptions options;
    private readonly string key = "storageApiAccessToken";


    public AccessTokenProvider(IAccessTokenProviderOptions options)
    {
        this.options = options;
        memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<string> GetAccessToken()
    {
        if (memoryCache.TryGetValue(key, out string token))
        {
            return token;
        }
        token = await GetBearerToken();
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(55)); // assume token is valid for 1 hour
        memoryCache.Set(key, token, cacheEntryOptions);
        return token;
    }


    private async Task<string> GetBearerToken()
    {
        
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, 
            $"https://login.microsoftonline.com/{options.TenantId}/oauth2/token");
        var collection = new List<KeyValuePair<string, string>>();
        collection.Add(new("grant_type", "client_credentials"));
        collection.Add(new("client_id", options.ClientId ));
        collection.Add(new("client_secret", options.ClientSecret));
        collection.Add(new("scope", $"api://{options.ClientSecret}/.default"));
        collection.Add(new("resource", $"api://{options.ClientSecret}"));
        var content = new FormUrlEncodedContent(collection);
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        return obj["access_token"];



    }

}


public class AccessTokenProviderOptions  : IAccessTokenProviderOptions
{
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string TenantId { get; set; }
}

public interface IAccessTokenProviderOptions
{
    string ClientId { get; set; }
    string ClientSecret { get; set; }
    string TenantId { get; set; }
}
