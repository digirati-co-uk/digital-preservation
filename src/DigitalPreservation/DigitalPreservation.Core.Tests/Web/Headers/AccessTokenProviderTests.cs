using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalPreservation.Core.Tests.Web.Headers;

/// <summary>
/// Pins the exact token request <see cref="AccessTokenProvider"/> sends to Entra — RFC-0001 Phase 2
/// option (a) and the two LPII-166 branch defects (comparison doc §6): an optional property must not
/// gate acquisition, and a configured <see cref="IAccessTokenProviderOptions.ResourceUri"/> must name
/// the TARGET resource on the v2.0 endpoint, not the caller's own registration on the v1.0 one.
/// </summary>
public class AccessTokenProviderTests
{
    private const string TenantId = "bdeaeda8-0000-0000-0000-000000000003";
    private const string ClientId = "a616cf42-0000-0000-0000-000000000001";
    private const string TargetResource = "api://84c62880-0000-0000-0000-000000000002";

    private static AccessTokenProviderOptions ValidOptions(string? resourceUri = null) => new()
    {
        TenantId = TenantId,
        ClientId = ClientId,
        ClientSecret = "test-secret",
        ResourceUri = resourceUri
    };

    private static AccessTokenProvider BuildProvider(IAccessTokenProviderOptions? options,
        HttpMessageHandler handler, ISystemClock? clock = null) =>
        new(NullLogger<AccessTokenProvider>.Instance, options, new StubHttpClientFactory(handler), clock);

    /// <summary>The IHttpClientFactory seam the production registrations use, over the test handler.</summary>
    private class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task WithoutResourceUri_MintsSelfTokenAgainstV1Endpoint()
    {
        // The pre-ResourceUri behaviour, pinned: this is the path every deployed environment takes
        // until a ResourceUri key is added to its config, so it must not change shape.
        var handler = new CapturingHandler();
        var sut = BuildProvider(ValidOptions(), handler);

        var token = await sut.GetAccessToken();

        token.Should().Be("test-token");
        handler.Request!.RequestUri!.ToString()
            .Should().Be($"https://login.microsoftonline.com/{TenantId}/oauth2/token");
        var form = handler.ParsedForm();
        form["grant_type"].Should().Be("client_credentials");
        form["client_id"].Should().Be(ClientId);
        form["client_secret"].Should().Be("test-secret");
        form["scope"].Should().Be($"api://{ClientId}/.default");
        form["resource"].Should().Be($"api://{ClientId}");
    }

    [Fact]
    public async Task WithResourceUri_RequestsTargetScopeAgainstV2Endpoint()
    {
        var handler = new CapturingHandler();
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var token = await sut.GetAccessToken();

        token.Should().Be("test-token");
        handler.Request!.RequestUri!.ToString()
            .Should().Be($"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token");
        var form = handler.ParsedForm();
        form["grant_type"].Should().Be("client_credentials");
        form["client_id"].Should().Be(ClientId);
        form["scope"].Should().Be($"{TargetResource}/.default",
            "the token must be requested FOR the target resource, not the caller's own registration");
        form.AllKeys.Should().NotContain("resource",
            "the v2.0 endpoint takes the target from scope; a resource parameter is the v1.0 shape");
    }

    [Fact]
    public async Task WithResourceUri_TrailingSlashDoesNotDoubleUp()
    {
        var handler = new CapturingHandler();
        var sut = BuildProvider(ValidOptions(TargetResource + "/"), handler);

        await sut.GetAccessToken();

        handler.ParsedForm()["scope"].Should().Be($"{TargetResource}/.default");
    }

    [Fact]
    public async Task NullResourceUri_DoesNotGateAcquisition()
    {
        // Regression pin for the LPII-166 branch defect: a reflection-based all-properties null
        // check meant that adding the optional property broke every deployment lacking the new key.
        var options = ValidOptions();
        options.ResourceUri.Should().BeNull();
        var handler = new CapturingHandler();
        var sut = BuildProvider(options, handler);

        var token = await sut.GetAccessToken();

        token.Should().NotBeNull();
        handler.Request.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, "secret", "tenant")]
    [InlineData("client", null, "tenant")]
    [InlineData("client", "secret", null)]
    public async Task MissingRequiredOption_ReturnsNullWithoutCalling(string? clientId, string? secret,
        string? tenantId)
    {
        var handler = new CapturingHandler();
        var sut = BuildProvider(new AccessTokenProviderOptions
            { ClientId = clientId, ClientSecret = secret, TenantId = tenantId }, handler);

        var token = await sut.GetAccessToken();

        token.Should().BeNull();
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        var handler = new CapturingHandler();
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var first = await sut.GetAccessToken();
        var second = await sut.GetAccessToken();

        second.Should().Be(first);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ATokenlessSuccessResponse_Throws_AndIsNotCached()
    {
        // A 2xx with no access_token must fail loudly on THIS call and leave nothing behind: a
        // cached null would strip the Authorization header from every machine call for ~56 minutes
        // (MachineAuthTokenInjector skips the header when the provider returns null).
        var handler = new CapturingHandler
            { ResponseBody = "{\"token_type\":\"Bearer\",\"expires_in\":3599}" };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var act = () => sut.GetAccessToken();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*without an access_token*");
        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.CallCount.Should().Be(2, "a failure must not be cached - every call retries the endpoint");
    }

    [Fact]
    public async Task AnEmptyToken_Throws()
    {
        var handler = new CapturingHandler
            { ResponseBody = "{\"token_type\":\"Bearer\",\"expires_in\":3599,\"access_token\":\"\"}" };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        await ((Func<Task>)(() => sut.GetAccessToken())).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ANonStringToken_Throws_TheControlledDiagnostic()
    {
        // {"access_token": 12345}: without the ValueKind check, GetString() throws .NET's generic
        // InvalidOperationException before the controlled path - no log, no property names.
        var handler = new CapturingHandler
            { ResponseBody = "{\"token_type\":\"Bearer\",\"access_token\":12345}" };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var act = () => sut.GetAccessToken();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*without an access_token*");
    }

    [Fact]
    public async Task WhitespaceCredentials_ReturnNull_WithoutCallingEntra()
    {
        // " " passes IsNullOrEmpty and would be SENT to Entra; the whitespace gate fails it
        // here instead, as ordinary misconfiguration (null, injector warns, no header).
        var handler = new CapturingHandler();
        var options = ValidOptions();
        options.ClientSecret = "   ";
        var sut = BuildProvider(options, handler);

        var token = await sut.GetAccessToken();

        token.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AWhitespaceResourceUri_IsUnset_AndTakesTheV1Path()
    {
        // "   " would otherwise build the invalid scope " /.default" on the v2.0 endpoint.
        var handler = new CapturingHandler();
        var sut = BuildProvider(ValidOptions("   "), handler);

        await sut.GetAccessToken();

        handler.Request!.RequestUri!.AbsoluteUri.Should().NotContain("/v2.0/");
        var form = HttpUtility.ParseQueryString(handler.FormBody!);
        form["resource"].Should().Be($"api://{ClientId}");
    }

    [Fact]
    public async Task AnErrorStatus_Throws_AndIsNotCached()
    {
        var handler = new CapturingHandler { StatusCode = HttpStatusCode.BadRequest };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var act = () => sut.GetAccessToken();

        await act.Should().ThrowAsync<HttpRequestException>();
        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task NumericExpiresIn_IsCachedUntilExpiresInMinusTheMargin()
    {
        var clock = new FakeClock();
        var handler = new CapturingHandler
            { ResponseBody = "{\"token_type\":\"Bearer\",\"expires_in\":3600,\"access_token\":\"test-token\"}" };
        var sut = BuildProvider(ValidOptions(TargetResource), handler, clock);

        await sut.GetAccessToken();
        handler.CallCount.Should().Be(1);

        // Still inside the cached lifetime (3600s - 5 minute margin = 3300s = 55 minutes).
        clock.UtcNow += TimeSpan.FromMinutes(54);
        await sut.GetAccessToken();
        handler.CallCount.Should().Be(1, "a second call inside the cached lifetime must not mint again");

        // Past it: the cache entry has expired, so this call mints a fresh token.
        clock.UtcNow += TimeSpan.FromMinutes(2);
        await sut.GetAccessToken();
        handler.CallCount.Should().Be(2, "a call past expires_in minus the margin must mint again");
    }

    [Fact]
    public async Task StringExpiresIn_FromTheV1Endpoint_IsTreatedTheSameAsNumeric()
    {
        var handler = new CapturingHandler
            { ResponseBody = "{\"token_type\":\"Bearer\",\"expires_in\":\"3600\",\"access_token\":\"test-token\"}" };
        var sut = BuildProvider(ValidOptions(), handler);

        var first = await sut.GetAccessToken();
        var second = await sut.GetAccessToken();

        first.Should().Be("test-token");
        second.Should().Be(first);
        handler.CallCount.Should().Be(1, "a string expires_in must be parsed and cached, not treated as malformed");
    }

    [Fact]
    public async Task MissingExpiresIn_IsStillReturned_ButOnlyCachedBriefly()
    {
        var clock = new FakeClock();
        var handler = new CapturingHandler { ResponseBody = "{\"token_type\":\"Bearer\",\"access_token\":\"test-token\"}" };
        var sut = BuildProvider(ValidOptions(TargetResource), handler, clock);

        var token = await sut.GetAccessToken();
        token.Should().Be("test-token", "the token is still usable even though its lifetime is unknown");
        handler.CallCount.Should().Be(1);

        clock.UtcNow += TimeSpan.FromMinutes(4);
        await sut.GetAccessToken();
        handler.CallCount.Should().Be(1, "within the conservative 5-minute lifetime, still served from cache");

        clock.UtcNow += TimeSpan.FromMinutes(2);
        await sut.GetAccessToken();
        handler.CallCount.Should().Be(2, "past the conservative lifetime, a token of unknown age must not be reused");
    }

    [Fact]
    public async Task MalformedExpiresIn_IsStillReturned_AndTreatedAsConservative()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = "{\"token_type\":\"Bearer\",\"expires_in\":\"not-a-number\",\"access_token\":\"test-token\"}"
        };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var token = await sut.GetAccessToken();

        token.Should().Be("test-token");
    }

    [Fact]
    public async Task ASmallExpiresIn_IsNotServedFromCache_AfterTheShortLifetime()
    {
        // 60s is inside the 5-minute margin, so this takes the same conservative-lifetime path as
        // a missing expires_in - the margin, not the raw value, decides whether it is trustworthy.
        var clock = new FakeClock();
        var handler = new CapturingHandler
            { ResponseBody = "{\"token_type\":\"Bearer\",\"expires_in\":60,\"access_token\":\"test-token\"}" };
        var sut = BuildProvider(ValidOptions(TargetResource), handler, clock);

        await sut.GetAccessToken();
        handler.CallCount.Should().Be(1);

        clock.UtcNow += TimeSpan.FromMinutes(6);
        await sut.GetAccessToken();
        handler.CallCount.Should().Be(2, "a 60-second expires_in must not be cached past the conservative margin");
    }

    [Fact]
    public async Task TwentyConcurrentMisses_ShareOneInFlightMint_AndAllReceiveTheToken()
    {
        var handler = new GatedHandler();
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var calls = Enumerable.Range(0, 20).Select(_ => sut.GetAccessToken()).ToArray();
        await WaitForCall(handler);
        handler.Release();

        var tokens = await Task.WhenAll(calls);

        tokens.Should().AllSatisfy(token => token.Should().Be("test-token"));
        handler.CallCount.Should().Be(1, "all 20 concurrent misses must share one in-flight mint");
    }

    [Fact]
    public async Task ASharedMintThatFails_EveryWaiterThrows_AndTheNextCallMintsAgain()
    {
        var handler = new GatedHandler { StatusCode = HttpStatusCode.BadRequest };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var calls = Enumerable.Range(0, 5).Select(_ => sut.GetAccessToken()).ToArray();
        await WaitForCall(handler);
        handler.Release();

        foreach (var call in calls)
        {
            var capturedCall = call;
            var act = () => capturedCall;
            await act.Should().ThrowAsync<HttpRequestException>();
        }
        handler.CallCount.Should().Be(1, "every waiter shared the one failed mint, so Entra was only asked once");

        var retry = () => sut.GetAccessToken();
        await retry.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(2, "a failed mint must not be remembered - the next call mints again");
    }

    [Fact]
    public async Task CancellingOneWaiter_OnlyThatWaiterThrows_TheOthersStillGetTheToken()
    {
        var handler = new GatedHandler();
        var sut = BuildProvider(ValidOptions(TargetResource), handler);
        using var cts = new CancellationTokenSource();

        var cancelledCall = sut.GetAccessToken(cts.Token);
        var otherCall1 = sut.GetAccessToken();
        var otherCall2 = sut.GetAccessToken();
        await WaitForCall(handler);

        cts.Cancel();
        var act = () => cancelledCall;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancelling a waiter must stop only that waiter's wait, not the shared mint");

        handler.Release();

        (await otherCall1).Should().Be("test-token");
        (await otherCall2).Should().Be("test-token");
        handler.CallCount.Should().Be(1, "the shared mint ran to completion for the callers that did not cancel");
    }

    [Fact]
    public void AddAccessTokenProvider_RegistersTheNamedClientWithA15SecondTimeout()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccessTokenProvider(ValidOptions(TargetResource));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(AccessTokenProvider.HttpClientName);

        client.Timeout.Should().Be(TimeSpan.FromSeconds(15),
            "HttpClient's 100-second default must not gate how fast a throttled Entra fails this call");
    }

    private static async Task WaitForCall(GatedHandler handler, int atLeast = 1)
    {
        var stopwatch = Stopwatch.StartNew();
        while (handler.CallCount < atLeast && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(5);
        }
        handler.CallCount.Should().BeGreaterThanOrEqualTo(atLeast,
            "the mint should have reached the gate well within this timeout");
    }

    /// <summary>A settable clock, injected into the provider's MemoryCache so expiry can be tested
    /// deterministically instead of by sleeping.</summary>
    private class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Blocks every request behind a shared gate until released, so concurrent callers genuinely
    /// overlap inside the single-flight window instead of completing one at a time.
    /// </summary>
    private class GatedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } =
            "{\"token_type\":\"Bearer\",\"expires_in\":3599,\"access_token\":\"test-token\"}";

        public void Release() => gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await gate.Task;
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Captures the outgoing token request and answers with a v2.0-shaped body — expires_in as a
    /// JSON number, which Dictionary&lt;string, string&gt; deserialization would reject.
    /// </summary>
    private class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? FormBody { get; private set; }
        public int CallCount { get; private set; }
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } =
            "{\"token_type\":\"Bearer\",\"expires_in\":3599,\"access_token\":\"test-token\"}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            FormBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }

        public System.Collections.Specialized.NameValueCollection ParsedForm() =>
            HttpUtility.ParseQueryString(FormBody ?? string.Empty);
    }
}
