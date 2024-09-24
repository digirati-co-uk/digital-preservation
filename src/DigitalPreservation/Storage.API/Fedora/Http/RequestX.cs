using System.Net.Http.Headers;
using Storage.API.Fedora.Model;
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
    
    public static HttpRequestMessage InTransaction(this HttpRequestMessage requestMessage, Transaction? transaction)
    {
        if(transaction != null)
        {
            requestMessage.Headers.Add(Transaction.HeaderName, transaction.Location.ToString());
        }
        return requestMessage;
    }
    
    public static HttpRequestMessage WithName(this HttpRequestMessage requestMessage, string? name)
    {
        if(requestMessage.Content == null && !string.IsNullOrWhiteSpace(name)) 
        {
            var turtle = MediaTypeHeaderValue.Parse("text/turtle");
            requestMessage.Content = new StringContent($"PREFIX dc: <http://purl.org/dc/elements/1.1/>  <> dc:title \"{name}\"", turtle);
        }
        return requestMessage;
    }
    
    public static HttpRequestMessage WithSlug(this HttpRequestMessage requestMessage, string slug) 
    {
        requestMessage.Headers.Add("slug", slug);
        return requestMessage;
    }
    
    public static HttpRequestMessage AsArchivalGroup(this HttpRequestMessage requestMessage)
    {
        // This tells Fedora it's an archival group:
        requestMessage.Headers.Add("Link", $"<{RepositoryTypes.ArchivalGroup}>;rel=\"type\"");

        // TODO: Can we now do this in one go and not require the subsequent PATCH? (See AsInsertTypePatch below)
        // But we want to assign an additional type that will be returned in contained resources
        //var stringContent = requestMessage.Content;
        //var turtle = MediaTypeHeaderValue.Parse("text/turtle");
        //var sparql = "  <> rdf:type <http://purl.org/dc/dcmitype/Collection> ;";
        //if (stringContent == null)
        //{
        //    stringContent = new StringContent(sparql, turtle);
        //}
        //else
        //{
        //    string oldContent = await stringContent.ReadAsStringAsync();
        //    stringContent = new StringContent($"{oldContent}\n{sparql}", turtle);
        //}
        //requestMessage.Content = stringContent;

        return requestMessage;
    }
    
    public static HttpRequestMessage AsInsertTypePatch(this HttpRequestMessage requestMessage, string type)
    {
        var sparql = $$"""
                       INSERT {   
                        <> a {{type}} .
                       }
                       WHERE { }
                       """;

        requestMessage.Content = new StringContent(sparql)
            .WithContentType("application/sparql-update");
        return requestMessage;
    }
    
    public static HttpContent WithContentType(this HttpContent httpContent, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            httpContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        return httpContent;
    }
}