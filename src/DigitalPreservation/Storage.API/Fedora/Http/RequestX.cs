using System.Net.Http.Headers;
using Storage.API.Fedora.Vocab;

namespace Storage.API.Fedora.Http;

internal static class RequestX
{
    public static HttpRequestMessage ForJsonLd(this HttpRequestMessage requestMessage)
    {
        requestMessage.Headers.Accept.Clear();
        var contentTypeHeader = new MediaTypeWithQualityHeaderValue("application/ld+json");
        contentTypeHeader.Parameters.Add(new NameValueHeaderValue("profile", JsonLdModes.Compacted));
        requestMessage.Headers.Accept.Add(contentTypeHeader);
        return requestMessage;
    }
    
    public static HttpRequestMessage WithContainedDescriptions(this HttpRequestMessage requestMessage)
    {
        requestMessage.Headers.Add("Prefer", $"return=representation; include=\"{Prefer.PreferContainedDescriptions}\"");
        return requestMessage;
    }
}