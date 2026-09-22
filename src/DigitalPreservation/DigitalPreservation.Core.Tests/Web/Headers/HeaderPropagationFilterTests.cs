using System.Net;
using System.Text;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Tests.Web.Headers;

/// <summary>
/// Builds clients through the REAL IHttpClientFactory with AddCorrelationIdHeaderPropagation and a
/// real AccessTokenProvider registered — the Preservation.API shape, which AccessTokenProviderTests'
/// stubbed factory structurally cannot see (the stub bypasses IHttpMessageHandlerBuilderFilter).
/// Pins the guard in HeaderPropagationMessageHandlerBuilderFilter: the global filter must not
/// decorate the token-endpoint client, because injecting a machine token into the token request
/// means minting a token to mint a token — unbounded synchronous recursion, a StackOverflowException,
/// and a dead process. If the guard regresses, the first test dies by stack overflow rather than a
/// failed assert; that is deliberate — so does the API, within a minute of startup.
/// </summary>
public class HeaderPropagationFilterTests
{
    private const string TokenResponse =
        "{\"token_type\":\"Bearer\",\"expires_in\":3599,\"access_token\":\"test-token\"}";

    private static ServiceProvider BuildPreservationApiShapedServices(
        RecordingHandler entra, RecordingHandler downstream)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // No ambient HttpContext, as in a hosted service (StorageImportJobsService) — the shape
        // that sends the filter down its SetMachineToken path on every outbound request.
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddHttpClient();
        services.AddCorrelationIdHeaderPropagation();
        services.AddSingleton<IAccessTokenProviderOptions>(new AccessTokenProviderOptions
        {
            TenantId = "bdeaeda8-0000-0000-0000-000000000003",
            ClientId = "a616cf42-0000-0000-0000-000000000001",
            ClientSecret = "test-secret"
        });
        services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();
        services.AddHttpClient(AccessTokenProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => entra);
        services.AddHttpClient("downstream")
            .ConfigurePrimaryHttpMessageHandler(() => downstream);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task MintingAToken_DoesNotRecurseThroughTheFilter()
    {
        var entra = new RecordingHandler(TokenResponse);
        await using var services = BuildPreservationApiShapedServices(entra, new RecordingHandler("{}"));
        var provider = services.GetRequiredService<IAccessTokenProvider>();

        var token = await provider.GetAccessToken();

        token.Should().Be("test-token");
        var request = entra.Requests.Should().ContainSingle().Subject;
        request.Authorization.Should().BeNull("the token request must not carry platform auth headers");
        request.HasMachineHeader.Should().BeFalse();
    }

    [Fact]
    public async Task OtherFactoryClients_StillGetTheMachineToken()
    {
        var entra = new RecordingHandler(TokenResponse);
        var downstream = new RecordingHandler("{}");
        await using var services = BuildPreservationApiShapedServices(entra, downstream);
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("downstream");

        // Task.Run: the filter blocks on GetAccessToken().Result, which is safe on a context-free
        // thread-pool thread (production) but could deadlock under a test SynchronizationContext.
        using var response = await Task.Run(() => client.GetAsync("https://storage.example/api/thing"));

        entra.Requests.Should().HaveCount(1, "one mint serves the downstream call");
        var request = downstream.Requests.Should().ContainSingle().Subject;
        request.Authorization.Should().Be("Bearer test-token");
        request.HasMachineHeader.Should().BeTrue();
    }

    /// <summary>Snapshots each request's auth-relevant headers at send time.</summary>
    private class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public record Seen(Uri? Uri, string? Authorization, bool HasMachineHeader);

        public List<Seen> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(
                request.RequestUri,
                request.Headers.TryGetValues("Authorization", out var auth) ? string.Join(" ", auth) : null,
                request.Headers.Contains(AuthFilterIdentifier.MachineHeaderName)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
