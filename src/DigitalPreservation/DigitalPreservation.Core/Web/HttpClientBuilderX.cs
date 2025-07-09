using System.Net;
using System.Net.Sockets;

namespace DigitalPreservation.Core.Web;

public static class HttpClientBuilderX
{
    // See https://github.com/dotnet/runtime/issues/77755
    // See https://github.com/dotnet/runtime/issues/24917
    // https://repost.aws/articles/ARjhCdFMoTTwmxenTc4B7elg/how-to-avoid-network-timeout-issues-when-invoking-long-running-lambda-functions-from-net6-applications-on-linux-platforms
    
    public static IHttpClientBuilder ConfigureTcpKeepAlive(
        this IHttpClientBuilder builder, 
        bool enabled,
        TimeSpan keepAliveTime, TimeSpan keepAliveInterval,
        int retryCount = 10)
    {
        if (enabled)
        {
            return builder.ConfigurePrimaryHttpMessageHandler(CreateHttpMessageHandler);
        }
        
        return builder;
    }
    
    
    private static HttpMessageHandler CreateHttpMessageHandler()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = ConfigureSocketTcpKeepAlive
        };

        return handler;
    }

    private static async ValueTask<Stream> ConfigureSocketTcpKeepAlive(
        SocketsHttpConnectionContext context,
        CancellationToken token)
    {
        var ipAddresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 300);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 15);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
            await socket.ConnectAsync(ipAddresses, context.DnsEndPoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async ValueTask<Stream> ConfigureSocketTcpKeepAliveDoesntWorkOnLinux(
        SocketsHttpConnectionContext context,
        CancellationToken token)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 120);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 60);   // was 10
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 60); // was 10
            await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
    
}