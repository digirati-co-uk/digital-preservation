using System.Net.Sockets;

namespace DigitalPreservation.Core.Web;

public static class HttpClientBuilderX
{
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