using System.Security.Cryptography;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace Preservation.API.IIIF;

public class TokenService(IMemoryCache memoryCache) : ITokenService
{
    // Sliding: a link nobody has used for 8 hours is dead. Absolute: however busy, a link dies after
    // a week - long enough for a deposit to be worked on over a weekend, since a viewer holding only
    // the tokenised URL has no way to mint a new one, but not forever.
    private static readonly TimeSpan SlidingLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public string GetToken(string key)
    {
        if (memoryCache.TryGetValue(key, out string? token) && token.HasText())
            return token;

        token = NewToken();
        // Sliding expiry alone meant a link in steady use never expired. The absolute limit caps a
        // token's life however often it is used; a client that outlives it asks for a new one.
        var lifetime = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(SlidingLifetime)
            .SetAbsoluteExpiration(AbsoluteLifetime);
        memoryCache.Set(key, token, lifetime);
        memoryCache.Set(token, key, lifetime);
        return token;
    }

    public string? GetKey(string token)
    {
        memoryCache.TryGetValue(token, out string? key);
        return key.HasText() ? key : null;
    }
}
