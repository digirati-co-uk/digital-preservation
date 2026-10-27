using System.Net.Http;
using DigitalPreservation.Core.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Tests.Web;

/// <summary>
/// ConfigurePlatformTcpKeepAlive bakes in the specific keepalive values (120s/30s/5 retries) that
/// every platform HTTP client reaching Fedora, Storage API or Preservation API through the
/// internet-facing ALB uses, replacing four identical .ConfigureTcpKeepAlive(true, ...) call sites
/// that each carried their own copy of the same rationale comment (issue #388 item 1.5). This
/// confirms it actually wires up a keepalive-configured SocketsHttpHandler as the client's primary
/// handler, not just that it compiles.
/// </summary>
public class HttpClientBuilderXTests
{
    [Fact]
    public void Wires_A_SocketsHttpHandler_With_A_ConnectCallback_Set()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("platform-client").ConfigurePlatformTcpKeepAlive();
        var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("platform-client");

        var socketsHandler = UnwrapToSocketsHttpHandler(handler);
        socketsHandler.Should().NotBeNull(
            "ConfigurePlatformTcpKeepAlive must set a primary handler with keepalive wired up");
        socketsHandler!.ConnectCallback.Should().NotBeNull(
            "the keepalive socket options are set inside this callback - see HttpClientBuilderX for why");
    }

    private static SocketsHttpHandler? UnwrapToSocketsHttpHandler(HttpMessageHandler handler)
    {
        var current = handler;
        while (current is DelegatingHandler { InnerHandler: not null } delegating)
        {
            current = delegating.InnerHandler;
        }
        return current as SocketsHttpHandler;
    }
}
