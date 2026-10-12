using System.Net;
using System.Text;
using System.Web;
using LeedsDlipServices.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Preservation.API.Tests.LeedsDlipServices;

/// <summary>
/// IdentityService built q, s and pid into relative URIs by interpolation, unescaped (September 2026
/// security review, issue #292 item 1). A value containing &amp;, #, ? or / could inject extra query
/// parameters or path segments on the Identity Service host. Pins that every caller-supplied value now
/// goes through Uri.EscapeDataString.
/// </summary>
public class IdentityServiceUrlEncodingTests
{
    private static readonly Uri PreservationRoot = new("https://preservation.example/");

    private static IdentityService BuildService(CapturingHandler handler) => new(
        NullLogger<IdentityService>.Instance,
        new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://identity.example/") },
        Options.Create(new IdentityOptions
        {
            Root = new Uri("https://identity.example/"),
            ApiKeyHeader = "X-API-KEY",
            ApiKeyValue = "test-key",
            PreservationRoot = PreservationRoot
        }));

    [Fact]
    public async Task SchemaQuery_EscapesAValueContainingQueryStringSyntax()
    {
        var handler = new CapturingHandler();
        var sut = BuildService(handler);

        await sut.GetIdentityByCatIrn("a&s=evil", CancellationToken.None);

        var query = HttpUtility.ParseQueryString(handler.Request!.RequestUri!.Query);
        query["q"].Should().Be("a&s=evil", "ParseQueryString decodes it back to the one value that was sent");
        query.GetValues("s").Should().ContainSingle().Which.Should().Be(SchemaAndValue.SchemaCatIrn);
        handler.Request.RequestUri.Query.Should().Contain("q=a%26s%3Devil",
            "the raw query string must carry the escaped form, not a second s parameter");
    }

    [Fact]
    public async Task SchemaQuery_EscapesAnArchivalGroupUri()
    {
        // GetIdentityByArchivalGroup passes a whole URI as q - its own ':' and '/' must be escaped too.
        var handler = new CapturingHandler();
        var sut = BuildService(handler);
        var archivalGroupUri = new Uri("https://preservation.example/archival-groups/abc");

        await sut.GetIdentityByArchivalGroup(archivalGroupUri, CancellationToken.None);

        handler.Request!.RequestUri!.Query.Should().Contain(Uri.EscapeDataString(archivalGroupUri.ToString()));
        handler.Request.RequestUri.Query.Should().NotContain("://",
            "an unescaped scheme separator would be sent as literal query-string syntax");
    }

    [Fact]
    public async Task DirectPidLookup_EscapesAPidContainingASlash()
    {
        var handler = new CapturingHandler();
        var sut = BuildService(handler);

        await sut.GetIdentityBySchema(new SchemaAndValue { Schema = SchemaAndValue.SchemaId, Value = "a/b" },
            CancellationToken.None);

        handler.Request!.RequestUri!.ToString().Should().EndWith("/ids/a%2Fb",
            "an unescaped '/' would be read by the Identity Service as an extra path segment");
    }

    /// <summary>Captures the outgoing request and always answers with an empty, well-formed result -
    /// these tests only pin the request shape, not the response handling.</summary>
    private class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var body = request.RequestUri!.AbsolutePath.Contains("/ids/")
                ? "{\"id\":\"x\",\"catirn\":\"x\",\"repositoryuri\":\"https://preservation.example/cc/x\"}"
                : "{\"results\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
