namespace DigitalPreservation.Core.Web.Headers;

/// <summary>
/// Middleware to ensure there is a CorrelationId (x-correlation-id) value in response.
/// Should be added early in pipeline to ensure it's available for use further down.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationHeaderKey = "x-correlation-id";

    public async Task InvokeAsync(HttpContext context)
    {
        var headerValue = context.TryGetHeaderValue(CorrelationHeaderKey, false) ?? Guid.NewGuid().ToString();

        // add the correlation id to the http response header
        AddCorrelationIdHeaderToResponse(context, headerValue);
 
        await next(context);
    }
    
    private static void AddCorrelationIdHeaderToResponse(HttpContext context, string correlationId) 
    {
        if (!context.Response.HasStarted && !context.Response.Headers.ContainsKey(CorrelationHeaderKey))
        {
            context.Response.Headers[CorrelationHeaderKey] = correlationId;
        }
    }
}