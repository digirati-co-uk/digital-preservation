using System.Net.Http.Headers;

namespace DigitalPreservation.Core.Web;

public static class HttpResponseMessageX
{
    /// <summary>
    /// Add x-requested-by to outgoing request headers
    /// </summary>
    /// <param name="headers">Current collection of headers</param>
    /// <param name="componentName">x-requested-by value</param>
    /// <returns>Modified headers</returns>
    public static HttpRequestHeaders WithRequestedBy(this HttpRequestHeaders headers, string componentName)
    {
        headers.Add("x-requested-by", componentName);
        return headers;
    }
    
    /// <summary>
    /// Add a user agent to outgoing request headers.
    ///
    /// See https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/User-Agent for syntax
    /// </summary>
    /// <param name="headers">Current collection of headers</param>
    /// <param name="userAgent">User-Agent value</param>
    /// <returns></returns>
    public static HttpRequestHeaders WithUserAgent(this HttpRequestHeaders headers, string userAgent)
    {
        headers.Add("User-Agent", userAgent);
        return headers;
    }
}