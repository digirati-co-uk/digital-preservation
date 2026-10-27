using Microsoft.Extensions.Caching.Memory;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// IsValidForDeposit replaces the "GetKey, then check HasText and GetSlug by hand" check that used
/// to be copied three times - twice in DepositsController, once in MediaController - with
/// differently-worded 401 detail text between them (issue #388 item 2.9). These pin the rule
/// itself: a token is only valid for the exact deposit it was minted for.
/// </summary>
public class TokenServiceTests
{
    private static TokenService Service() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void IsValidForDeposit_ReturnsTrue_ForATokenMintedForThatDeposit()
    {
        var service = Service();
        var token = service.GetToken("someuser/deposit/dep-1");

        service.IsValidForDeposit(token, "dep-1").Should().BeTrue();
    }

    [Fact]
    public void IsValidForDeposit_ReturnsFalse_ForAnUnknownToken()
    {
        Service().IsValidForDeposit("never-minted", "dep-1").Should().BeFalse();
    }

    [Fact]
    public void IsValidForDeposit_ReturnsFalse_WhenTheTokenWasMintedForADifferentDeposit()
    {
        var service = Service();
        var token = service.GetToken("someuser/deposit/dep-99");

        service.IsValidForDeposit(token, "dep-1").Should().BeFalse();
    }

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
