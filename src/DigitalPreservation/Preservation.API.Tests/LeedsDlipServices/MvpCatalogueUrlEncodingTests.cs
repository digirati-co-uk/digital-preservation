using System.Net;
using System.Text;
using LeedsDlipServices.MVPCatalogueApi;
using Microsoft.Extensions.Options;

namespace Preservation.API.Tests.LeedsDlipServices;

/// <summary>
/// MvpCatalogue built its request URI as {template}{pid} by interpolation, unescaped (September 2026
/// security review, issue #292 item 1). Pins that pid now goes through Uri.EscapeDataString, matching
/// IdentityServiceUrlEncodingTests for the Identity Service's own ids/{pid} request.
/// </summary>
public class MvpCatalogueUrlEncodingTests
{
    private static MvpCatalogue BuildCatalogue(CapturingHandler handler) => new(
        new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://catalogue.example/") },
        Options.Create(new CatalogueOptions
        {
            Root = new Uri("https://catalogue.example/"),
            QueryTemplate = "/imu/utilities/getIIIFData.php?pid=",
            ApiKeyHeader = "X-API-KEY",
            ApiKeyValue = "test-key"
        }));

    [Fact]
    public async Task GetCatalogueRecordByPid_EscapesAPidContainingQueryStringSyntax()
    {
        var handler = new CapturingHandler();
        var sut = BuildCatalogue(handler);

        await sut.GetCatalogueRecordByPid("a&evil=1", CancellationToken.None);

        handler.Request!.RequestUri!.Query.Should().Be("?pid=a%26evil%3D1",
            "an unescaped '&' would inject a second query parameter onto the catalogue host");
    }

    [Fact]
    public async Task GetCatalogueRecordByPid_EscapesAPidContainingASlash()
    {
        var handler = new CapturingHandler();
        var sut = BuildCatalogue(handler);

        await sut.GetCatalogueRecordByPid("a/b", CancellationToken.None);

        handler.Request!.RequestUri!.Query.Should().Be("?pid=a%2Fb");
    }

    /// <summary>Captures the outgoing request; these tests only pin the request shape.</summary>
    private class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"data\":{\"Title\":\"x\"}}", Encoding.UTF8, "application/json")
            });
        }
    }
}
