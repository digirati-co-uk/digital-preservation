using System.Security.Cryptography;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace Preservation.API.IIIF;

public class TokenService(IMemoryCache memoryCache) : ITokenService
{
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public string GetToken(string key)
    {
        if (memoryCache.TryGetValue(key, out string? token) && token.HasText())
            return token;

        token = NewToken();
        var keyOpts   = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(8));
        var tokenOpts = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(8));
        memoryCache.Set(key, token, keyOpts);
        memoryCache.Set(token, key, tokenOpts);
        return token;
    }

    public string? GetKey(string token)
    {
        memoryCache.TryGetValue(token, out string? key);
        return key.HasText() ? key : null;
    }
}
