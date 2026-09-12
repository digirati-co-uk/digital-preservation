using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Storage.API.Tests.Integration;

/// <summary>
/// Characterises which <c>AzureAd</c> config shapes make the API accept which token audiences,
/// through the REAL <c>AddMicrosoftIdentityWebApi</c> registration used by Storage.API and
/// Preservation.API Program.cs (the <see cref="ApiAuthorizationStackTests"/> stub handler bypasses
/// JwtBearer entirely, so it cannot cover this).
///
/// This is the test RFC-0001 §6 Phase 0 calls for: a mis-shaped audience config deploys cleanly and
/// only fails when tokens arrive. Specifically it pins down that:
///
///  1. the dual-audience transition shape that works is
///     <c>AzureAd:TokenValidationParameters:ValidAudiences</c> — both audiences accepted;
///  2. the singular <c>Audience</c> key (today's production shape) accepts only that audience;
///  3. a top-level plural <c>Audiences</c> key — which an earlier RFC draft wrongly prescribed —
///     binds to NOTHING: Microsoft.Identity.Web falls back to ClientId-derived default audiences,
///     so the transitional audience is rejected and every current caller would be locked out.
///
/// The host is hermetic: a static (empty) <see cref="ConfigurationManager{T}"/> replaces the Entra
/// metadata endpoint, tokens are signed with a local symmetric key registered as the issuer signing
/// key, and issuer validation is switched off. Audience validation — the subject under test — runs
/// exactly as Microsoft.Identity.Web 3.8.3 configures it.
/// </summary>
[Trait("Category", "Integration")]
public class AudienceValidationTests
{
    // Deliberately fake GUIDs, shaped like the real registrations in RFC-0001 §3.
    private const string ApiClientId = "84c62880-0000-0000-0000-000000000002";
    private const string TransitionalAudience = "api://a616cf42-0000-0000-0000-000000000001"; // the shared UI registration
    private const string OwnAudience = $"api://{ApiClientId}";                                // the API's own App ID URI
    private const string ForeignAudience = "api://ffffffff-0000-0000-0000-00000000000f";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("integration-test-signing-key-0123456789abcdef"));

    private static readonly Dictionary<string, string?> BaseAzureAd = new()
    {
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = "bdeaeda8-0000-0000-0000-000000000003",
        ["AzureAd:ClientId"] = ApiClientId,
    };

    /// <summary>The RFC-0001 Phase 0 transition shape (mirrors appsettings.Example.json).</summary>
    private static Dictionary<string, string?> PhaseZeroShape => new(BaseAzureAd)
    {
        ["AzureAd:TokenValidationParameters:ValidAudiences:0"] = TransitionalAudience,
        ["AzureAd:TokenValidationParameters:ValidAudiences:1"] = OwnAudience,
    };

    /// <summary>Today's production shape: the singular Audience key.</summary>
    private static Dictionary<string, string?> CurrentShape => new(BaseAzureAd)
    {
        ["AzureAd:Audience"] = TransitionalAudience,
    };

    /// <summary>The shape an earlier RFC draft wrongly prescribed: a top-level plural Audiences key.</summary>
    private static Dictionary<string, string?> BrokenPluralShape => new(BaseAzureAd)
    {
        ["AzureAd:Audiences:0"] = TransitionalAudience,
        ["AzureAd:Audiences:1"] = OwnAudience,
    };

    private static TestServer BuildServer(Dictionary<string, string?> azureAdSettings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(azureAdSettings).Build();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                // Exactly as registered in Storage.API / Preservation.API Program.cs.
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

                // Hermetic test seam only — audience handling is left untouched.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration());
                    // A configured IssuerValidator delegate (Microsoft.Identity.Web installs its
                    // AadIssuerValidator) runs even when ValidateIssuer is false, so drop it too.
                    options.TokenValidationParameters.ValidateIssuer = false;
                    options.TokenValidationParameters.IssuerValidator = null;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                });

                services.AddAuthorization();
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                    endpoints.MapGet("/probe", context => context.Response.WriteAsync("ok"))
                        .RequireAuthorization());
            });

        return new TestServer(builder);
    }

    private static async Task<HttpStatusCode> Probe(TestServer server, string audience)
    {
        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(audience));
        var response = await client.SendAsync(request);

        // On 401 the JwtBearer challenge carries the validation error; surface it so an unexpected
        // rejection explains itself instead of reading as an audience failure.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var challenge = response.Headers.WwwAuthenticate.ToString();
            challenge.Should().ContainEquivalentOf("audience",
                "the only thing this harness leaves for validation to reject is the audience, " +
                $"but the challenge was: {challenge}");
        }

        return response.StatusCode;
    }

    private static string MintToken(string audience)
    {
        // Shaped like a v1.0 app-only token (ver, azp), carrying a roles claim as a per-caller
        // token will after RFC-0001 Phase 1 — also keeps the test immune to Microsoft.Identity.Web's
        // scp-or-roles requirement, so the only thing deciding acceptance here is the audience.
        var descriptor = new SecurityTokenDescriptor
        {
            Audience = audience,
            Issuer = "https://sts.test/",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("ver", "1.0"),
                new Claim("azp", "11111111-1111-1111-1111-111111111111"),
                new Claim("roles", "Preservation.Call"),
            }),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Theory]
    [InlineData(TransitionalAudience)]
    [InlineData(OwnAudience)]
    public async Task PhaseZeroShape_AcceptsBothAudiences(string audience)
    {
        // The transition depends on this: during Phases 1-3 un-migrated callers present the
        // transitional audience while migrated callers present the API's own.
        using var server = BuildServer(PhaseZeroShape);

        var status = await Probe(server, audience);

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PhaseZeroShape_RejectsForeignAudience()
    {
        using var server = BuildServer(PhaseZeroShape);

        var status = await Probe(server, ForeignAudience);

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CurrentShape_AcceptsOnlyTheConfiguredAudience()
    {
        // Today's singular-Audience config: the API's own App ID URI is NOT accepted — which is
        // why Phase 0 must widen the audience list before any caller can be repointed.
        using var server = BuildServer(CurrentShape);

        (await Probe(server, TransitionalAudience)).Should().Be(HttpStatusCode.OK);
        (await Probe(server, OwnAudience)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TopLevelAudiencesKey_BindsToNothing_AndLocksOutCurrentCallers()
    {
        // Regression pin for the RFC-0001 Phase 0 defect: a top-level plural Audiences key is not a
        // property of any bound options type, so it configures nothing...
        using var server = BuildServer(BrokenPluralShape);

        var options = server.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        options.Audience.Should().BeNull();
        options.TokenValidationParameters.ValidAudience.Should().BeNull();
        options.TokenValidationParameters.ValidAudiences.Should().BeNull();

        // ...and Microsoft.Identity.Web then falls back to ClientId-derived default audiences:
        // the transitional audience every current caller presents is REJECTED (the outage), while
        // the API's own audience is accepted — the exact inverse of the intended transition state.
        (await Probe(server, TransitionalAudience)).Should().Be(HttpStatusCode.Unauthorized);
        (await Probe(server, OwnAudience)).Should().Be(HttpStatusCode.OK);
    }
}
