using Microsoft.Extensions.Caching.Memory;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// NewToken used Convert.ToHexString(...).ToLowerInvariant() to build its token, one of three ways
/// the codebase produced lowercase hex (issue #388 item 3.4), now Convert.ToHexStringLower. No
/// prior test pinned the token's actual format, only its round-trip behaviour via GetKey.
/// </summary>
public class TokenServiceTests
{
    private static TokenService Service() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void GetToken_Returns_A_32_Character_Lowercase_Hex_String()
    {
        var token = Service().GetToken("deposit/dep-1");

        token.Should().MatchRegex("^[0-9a-f]{32}$", "16 random bytes, hex-encoded with no uppercase");
    }

    [Fact]
    public void GetToken_Is_Stable_For_The_Same_Key()
    {
        var service = Service();

        service.GetToken("deposit/dep-1").Should().Be(service.GetToken("deposit/dep-1"));
    }
}
