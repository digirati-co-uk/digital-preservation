using System.Net;
using System.Net.Sockets;

namespace DigitalPreservation.Core.Web;

public static class HttpClientBuilderX
{
    // See https://github.com/dotnet/runtime/issues/77755
    // See https://github.com/dotnet/runtime/issues/24917
    // https://repost.aws/articles/ARjhCdFMoTTwmxenTc4B7elg/how-to-avoid-network-timeout-issues-when-invoking-long-running-lambda-functions-from-net6-applications-on-linux-platforms

    // Why this exists (issue #350): these clients reach Fedora, Storage API and Preservation API
    // through an internet-facing ALB, so their traffic leaves through a NAT gateway and comes back
    // in. The NAT gateway drops a connection's mapping after it has been silent for its fixed 350s
    // idle timeout - confirmed as the cause of both a large Fedora GET failing with a broken pipe
    // and a large commit PUT appearing to hang, although Fedora had in both cases already written
    // the response; the ALB just couldn't deliver it back through the expired mapping. A TCP
    // keepalive probe sent well inside that 350s window keeps the mapping alive for as long as the
    // connection is genuinely still in use.
    //
    // This sets keepalive via socket options inside a ConnectCallback, not via
    // SocketsHttpHandler.KeepAlivePing* (a previous version of this comment said otherwise):
    // KeepAlivePing only applies to HTTP/2, and these clients all use HTTP/1.1. The ConnectCallback
    // resolves the host to an IP address itself and connects a raw Socket, because setting
    // keepalive on a socket SocketsHttpHandler connected by hostname does not take effect on Linux
    // (see the runtime issues above) - don't go back to configuring keepalive without this
    // indirection.
    public static IHttpClientBuilder ConfigureTcpKeepAlive(
        this IHttpClientBuilder builder,
        bool enabled,
        TimeSpan keepAliveTime, TimeSpan keepAliveInterval,
        int retryCount = 10)
    {
        if (!enabled)
        {
            return builder;
        }

        return builder.ConfigurePrimaryHttpMessageHandler(
            () => CreateHttpMessageHandler(keepAliveTime, keepAliveInterval, retryCount));
    }

    /// <summary>
    /// The specific keepalive values every platform HTTP client reaching Fedora, Storage API or
    /// Preservation API through the internet-facing ALB uses (issue #383 review point 7; previously
    /// repeated at all four call sites, each carrying its own copy of this comment - consolidated
    /// here per issue #388 item 1.5). 120s before the first probe, a 30s interval, and 5 retries -
    /// rather than the previously hard-coded 300/15/5, or a naive 120/60/60 - means a dead
    /// connection now actually gets noticed in about 120 + 5*30 = 270s: comfortably under the NAT
    /// gateway's 350s mapping timeout, and in a few minutes rather than the ~62 minutes a naive
    /// 120/60/60 would take.
    /// </summary>
    public static IHttpClientBuilder ConfigurePlatformTcpKeepAlive(this IHttpClientBuilder builder) =>
        builder.ConfigureTcpKeepAlive(true, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(30), 5);


    private static SocketsHttpHandler CreateHttpMessageHandler(
        TimeSpan keepAliveTime, TimeSpan keepAliveInterval, int retryCount)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (context, token) =>
                ConfigureSocketTcpKeepAlive(context, keepAliveTime, keepAliveInterval, retryCount, token)
        };

        return handler;
    }

    private static async ValueTask<Stream> ConfigureSocketTcpKeepAlive(
        SocketsHttpConnectionContext context,
        TimeSpan keepAliveTime,
        TimeSpan keepAliveInterval,
        int retryCount,
        CancellationToken token)
    {
        var ipAddresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)keepAliveTime.TotalSeconds);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, (int)keepAliveInterval.TotalSeconds);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, retryCount);
            await socket.ConnectAsync(ipAddresses, context.DnsEndPoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

}