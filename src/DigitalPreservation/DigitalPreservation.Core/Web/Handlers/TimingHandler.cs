using System.Diagnostics;

namespace DigitalPreservation.Core.Web.Handlers;

/// <summary>
/// A basic delegating handler that logs timing notifications
/// Log level used depends on how long the request took.
/// </summary>
public class TimingHandler(ILogger<TimingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var path = request.RequestUri!.GetLeftPart(UriPartial.Path);
        logger.LogDebug("Calling {Uri}..", path);
        
        var result = await base.SendAsync(request, cancellationToken);
        
        sw.Stop();
        var elapsedMilliseconds = sw.ElapsedMilliseconds;
        var logLevel = GetLogLevel(elapsedMilliseconds);
        logger.Log(logLevel, "Request to {Uri} completed with status {StatusCode} in {Elapsed}ms", path,
            result.StatusCode, elapsedMilliseconds);
        return result;
    }

    private static LogLevel GetLogLevel(long elapsedMilliseconds)
        => elapsedMilliseconds switch
        {
            >= 10000 => LogLevel.Warning,
            >= 3000 => LogLevel.Information,
            _ => LogLevel.Debug
        };
}