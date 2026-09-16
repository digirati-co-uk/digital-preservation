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
        new(NullLogger<AccessTokenProvider>.Instance, options, handler);

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

    /// <summary>
    /// Captures the outgoing token request and answers with a v2.0-shaped body — expires_in as a
    /// JSON number, which Dictionary&lt;string, string&gt; deserialization would reject.
    /// </summary>
    private class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? FormBody { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            FormBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"token_type\":\"Bearer\",\"expires_in\":3599,\"access_token\":\"test-token\"}",
                    Encoding.UTF8, "application/json")
            };
        }

        public System.Collections.Specialized.NameValueCollection ParsedForm() =>
            HttpUtility.ParseQueryString(FormBody ?? string.Empty);
    }
}
