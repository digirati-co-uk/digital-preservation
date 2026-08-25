using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DigitalPreservation.Core.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Storage.API.Tests.Integration;

/// <summary>
/// Characterises the API authorization stack used by Storage.API and Preservation.API, to settle
/// whether the <c>X-Client-Identity</c> header is an authentication bypass.
///
/// It reproduces the global filter registration from <c>Storage.API/Program.cs</c> (identical block
/// in <c>Preservation.API/Program.cs</c>):
/// <code>
///     config.Filters.Add(new AuthorizeFilter());      // == [Authorize]: requires an authenticated caller
///     config.Filters.Add(new AuthFilterIdentifier()); // attribution: reads X-Client-Identity AFTER auth
/// </code>
///
/// The real API hosts can't exercise this directly because <c>appsettings.Testing.json</c> sets
/// <c>FeatureFlags:DisableAuth=true</c>, which skips registering these filters entirely. So we stand
/// up a minimal pipeline with the exact same two filters plus a stub authentication handler standing
/// in for the Entra JWT bearer handler.
///
/// Conclusion (see tests): <c>new AuthorizeFilter()</c> is NOT a no-op — its parameterless ctor passes
/// a default <c>AuthorizeAttribute</c>, so it enforces authentication. <c>X-Client-Identity</c> is only
/// honoured once a caller is already authenticated, and then only as the display label the call is
/// attributed to. So it is not an auth bypass; the residual issue is that an already-authenticated
/// machine caller controls its own attribution label (see the last test).
/// </summary>
[Trait("Category", "Integration")]
public class ApiAuthorizationStackTests
{
    private const string SecurePath = "/secure";

    private static TestServer BuildServer(IClientDirectory? clients = null, CapturingLoggerProvider? logs = null)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>("Test", _ => { });
                services.AddAuthorization();
                if (logs != null)
                {
                    services.AddLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                        logging.AddProvider(logs);
                    });
                }
                if (clients != null)
                {
                    // RFC-0001 Phase 0: when a KnownClients allow-list is registered, AuthFilterIdentifier
                    // resolves the signed azp ahead of the X-Client-Identity header.
                    services.AddSingleton(clients);
                }
                services
                    .AddControllers(config =>
                    {
                        // Exactly as registered in Storage.API / Preservation.API Program.cs.
                        config.Filters.Add(new AuthorizeFilter());
                        config.Filters.Add(new AuthFilterIdentifier());
                    })
                    .AddApplicationPart(typeof(ApiAuthorizationStackTests).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });

        return new TestServer(builder);
    }

    [Fact]
    public async Task Request_WithNoCredentials_IsRejected()
    {
        // Authentication is enforced: the parameterless AuthorizeFilter == [Authorize].
        using var server = BuildServer();
        var client = server.CreateClient();

        var response = await client.GetAsync(SecurePath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithOnlyClientIdentityHeaderAndNoToken_IsRejected()
    {
        // The key result: X-Client-Identity is NOT an authentication bypass. Without a validated
        // token the AuthorizeFilter rejects the request before AuthFilterIdentifier's machine branch
        // can attribute it.
        using var server = BuildServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
        request.Headers.Add(AuthFilterIdentifier.MachineHeaderName, "totally-not-the-real-iiif-builder");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithAuthenticatedUser_IsAuthorized()
    {
        // Normal human path: an authenticated principal that carries a display name passes via the
        // "valid user" branch of AuthFilterIdentifier.
        using var server = BuildServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
        request.Headers.Add(StubAuthHandler.AuthenticateUserHeader, "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_FromAuthenticatedMachine_IsAttributedToTheClientIdentityHeader()
    {
        // The residual (lower-severity) concern: a client-credentials token authenticates but carries
        // no display name, so AuthFilterIdentifier falls back to the X-Client-Identity header for the
        // identity the call is attributed to (logs, METS authorship). That label is self-asserted, so
        // one authenticated machine caller can attribute its calls to a different one. It is NOT a
        // privilege escalation — the caller had to present a valid token first.
        using var server = BuildServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
        request.Headers.Add(StubAuthHandler.AuthenticateMachineHeader, "true");
        request.Headers.Add(AuthFilterIdentifier.MachineHeaderName, "some-other-service");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attributedIdentity = await response.Content.ReadAsStringAsync();
        attributedIdentity.Should().Be("some-other-service");
    }

    [Fact]
    public async Task Request_WithV1AuthenticatedUser_IsAuthorized()
    {
        // A v1.0-format delegated token carries upn/unique_name but no preferred_username. The
        // shared IsHumanCaller predicate must class it human, so no machine Name claim is injected.
        using var server = BuildServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
        request.Headers.Add(StubAuthHandler.AuthenticateV1UserHeader, "true");
        // A spoofed header must be irrelevant for a human caller.
        request.Headers.Add(AuthFilterIdentifier.MachineHeaderName, "not-a-machine-really");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("authenticated-user");
    }

    [Fact]
    public async Task FallbackWarning_IsLoggedOncePerAppId_ThenAtDebug()
    {
        // RFC-0001 Phase 0: the header-fallback warning is the Phase 2 migration diagnostic, but the
        // platform's own service-to-service calls take this path on every request until they migrate,
        // so it must not warn per request.
        var logs = new CapturingLoggerProvider();
        using var server = BuildServer(logs: logs);
        var client = server.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
            request.Headers.Add(StubAuthHandler.AuthenticateMachineHeader, "true");
            request.Headers.Add(AuthFilterIdentifier.MachineHeaderName, "legacy-caller");
            (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var fallbackEntries = logs.Entries
            .Where(e => e.Message.Contains("not resolved from KnownClients")).ToList();
        fallbackEntries.Where(e => e.Level == LogLevel.Warning).Should().HaveCount(1,
            "the first unresolved request for an appId warns");
        fallbackEntries.Where(e => e.Level == LogLevel.Debug).Should().HaveCount(1,
            "subsequent requests from the same appId log at Debug");
    }

    [Fact]
    public async Task KnownMachine_DoesNotLogFallbackWarning()
    {
        var clients = new ClientDirectory(new Dictionary<string, ClientProfile>
        {
            ["11111111-1111-1111-1111-111111111111"] = new() { Name = "iiif-builder" }
        });
        var logs = new CapturingLoggerProvider();
        using var server = BuildServer(clients, logs);
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
        request.Headers.Add(StubAuthHandler.AuthenticateMachineHeader, "true");
        request.Headers.Add(AuthFilterIdentifier.MachineHeaderName, "spoofed");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        logs.Entries.Should().NotContain(e => e.Message.Contains("not resolved from KnownClients"));
    }

    [Fact]
    public async Task Request_FromKnownMachine_IsAttributedToTheSignedTokenIdentity_NotTheHeader()
    {
        // RFC-0001 Phase 0: once the caller's azp is in the KnownClients allow-list, the signed token
        // identity wins over the self-asserted X-Client-Identity header. Here the token carries
        // azp = 1111... (see StubAuthHandler) and the request spoofs a different header; attribution
        // must be the configured name, proving the header is no longer the discriminator.
        var clients = new ClientDirectory(new Dictionary<string, ClientProfile>
        {
            ["11111111-1111-1111-1111-111111111111"] = new() { Name = "iiif-builder" }
        });
        using var server = BuildServer(clients);
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, SecurePath);
        request.Headers.Add(StubAuthHandler.AuthenticateMachineHeader, "true");
        request.Headers.Add(AuthFilterIdentifier.MachineHeaderName, "some-other-service");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attributedIdentity = await response.Content.ReadAsStringAsync();
        attributedIdentity.Should().Be("iiif-builder");
    }
}

/// <summary>
/// Stand-in for the Entra JWT bearer handler. Authenticates only when one of its trigger headers is
/// present; otherwise <see cref="AuthenticateResult.NoResult"/>, exactly as JwtBearer behaves when no
/// (or an invalid) token is supplied.
/// </summary>
public class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Authenticate as a human user that carries a display name.</summary>
    public const string AuthenticateUserHeader = "Test-Authenticate-User";

    /// <summary>Authenticate as a human user with a v1.0-format token (upn, no preferred_username).</summary>
    public const string AuthenticateV1UserHeader = "Test-Authenticate-V1-User";

    /// <summary>Authenticate as a client-credentials machine caller with NO display name.</summary>
    public const string AuthenticateMachineHeader = "Test-Authenticate-Machine";

    public StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(AuthenticateUserHeader))
        {
            // preferred_username is what Microsoft.Identity.Web's GetDisplayName() reads first.
            return Authenticated(new Claim("preferred_username", "real-user@leeds.ac.uk"));
        }

        if (Request.Headers.ContainsKey(AuthenticateV1UserHeader))
        {
            // v1.0 delegated tokens identify the user by upn/unique_name instead.
            return Authenticated(new Claim("upn", "v1-user@leeds.ac.uk"));
        }

        if (Request.Headers.ContainsKey(AuthenticateMachineHeader))
        {
            // A client-credentials token: valid, but no display-name claim.
            return Authenticated(new Claim("azp", "11111111-1111-1111-1111-111111111111"));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    private static Task<AuthenticateResult> Authenticated(params Claim[] claims)
    {
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
            "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>Collects log entries so tests can assert on levels and messages.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get { lock (entries) return entries.ToList(); }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner.entries) owner.entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

[ApiController]
[Route("secure")]
public class SecureTestController : ControllerBase
{
    // Returns the identity the request was attributed to. For a machine caller this is the "Name"
    // claim that AuthFilterIdentifier synthesises straight from the X-Client-Identity header.
    [HttpGet]
    public IActionResult Get() => Ok(User.FindFirst("Name")?.Value ?? "authenticated-user");
}
