using System.Security.Cryptography;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace Preservation.API.IIIF;

public class TokenService(IMemoryCache memoryCache) : ITokenService
{
    private static readonly TimeSpan SlidingLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(24);

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public string GetToken(string key)
    {
        if (memoryCache.TryGetValue(key, out string? token) && token.HasText())
            return token;

        token = NewToken();
        // Sliding expiry alone meant a link in steady use never expired. The absolute limit caps a
        // token's life however often it is used; a client that outlives it asks for a new one.
        var keyOpts   = new MemoryCacheEntryOptions().SetSlidingExpiration(SlidingLifetime).SetAbsoluteExpiration(AbsoluteLifetime);
        var tokenOpts = new MemoryCacheEntryOptions().SetSlidingExpiration(SlidingLifetime).SetAbsoluteExpiration(AbsoluteLifetime);
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
