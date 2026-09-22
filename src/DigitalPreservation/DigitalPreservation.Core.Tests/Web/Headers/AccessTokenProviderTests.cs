using System.Net;
using System.Text;
using System.Web;
using DigitalPreservation.Core.Web.Headers;
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
        CapturingHandler handler) =>
        new(NullLogger<AccessTokenProvider>.Instance, options, new StubHttpClientFactory(handler));

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
    public async Task AnErrorStatus_Throws_AndIsNotCached()
    {
        var handler = new CapturingHandler { StatusCode = HttpStatusCode.BadRequest };
        var sut = BuildProvider(ValidOptions(TargetResource), handler);

        var act = () => sut.GetAccessToken();

        await act.Should().ThrowAsync<HttpRequestException>();
        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(2);
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
