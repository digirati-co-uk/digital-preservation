using Microsoft.AspNetCore.Mvc.Testing;
using Test.Helpers;

namespace Preservation.API.Tests.Integration;

/// <summary>
/// GetDepositAsIIIF had no [RequireFeatureFlag], only its redirect target did. With the flag off, a
/// caller following the redirect got a bare 401 from an [AllowAnonymous] endpoint - which looks
/// like an authentication failure - after a token had already been minted for nothing. Gating the
/// redirecting endpoint itself refuses up front, and RequireFeatureFlagAttribute now answers 404
/// rather than 401, since a disabled feature isn't an authentication problem (issue #274 item 4).
/// </summary>
[Trait("Category", "Integration")]
public class IiifRedirectFeatureFlagTests
{
    private static HttpClient NoRedirectClient(DigitalPreservationAppFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Get_Returns404_WithNoLocationHeader_WhenTheFlagIsOff()
    {
        // appsettings.Testing.json leaves EnableIiifMediaEndpoints unset (off) and has DisableAuth: true.
        using var factory = new DigitalPreservationAppFactory<Program>();
        var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/deposits/abc/iiif");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Get_Returns302_WhenTheFlagIsOn()
    {
        using var factory = new DigitalPreservationAppFactory<Program>()
            .WithConfigValue("FeatureFlags:EnableIiifMediaEndpoints", "true");
        var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/deposits/abc/iiif");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
    }
}
