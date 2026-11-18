using System.Net.Http.Headers;
using DigitalPreservation.Utils;
using Storage.API.Fedora.Model;
using Storage.API.Fedora.Vocab;

namespace Storage.API.Fedora.Http;

public static class RequestX
{
    private const string SparqlUpdateContentType = "application/sparql-update";

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
        if(!string.IsNullOrWhiteSpace(name))
        {
            var escaped = name.EscapeForLiteralRdf(true);
            requestMessage.AppendRdf("dc", RepositoryTypes.DublinCoreElementsNamespace, $"<> dc:title \"{escaped}\"");
        }
        return requestMessage;
    }
    
    public static HttpRequestMessage WithCreatedBy(this HttpRequestMessage requestMessage, string createdBy)
    {
        // can only set this in the body, so a binary can't be done this way
        if(!string.IsNullOrWhiteSpace(createdBy)) 
        {
            var escaped = createdBy.EscapeForLiteralRdf(true);
            requestMessage.AppendRdf("fedora", RepositoryTypes.FedoraNamespace, $"<> fedora:createdBy \"{escaped}\"");
            requestMessage.AppendRdf("fedora", RepositoryTypes.FedoraNamespace, $"<> fedora:lastModifiedBy \"{escaped}\""); 
        }
        return requestMessage;
    }

    
    public static HttpRequestMessage WithLastModifiedBy(this HttpRequestMessage requestMessage, string lastModifiedBy)
    {
        // can only set this in the body, so a binary can't be done this way
        if(!string.IsNullOrWhiteSpace(lastModifiedBy)) 
        {
            var escaped = lastModifiedBy.EscapeForLiteralRdf(true);
            requestMessage.AppendRdf("fedora", RepositoryTypes.FedoraNamespace, $"<> fedora:lastModifiedBy \"{escaped}\""); 
        }
        return requestMessage;
    }

    
    public static HttpRequestMessage WithSlug(this HttpRequestMessage requestMessage, string escapedSlug)
    {
        requestMessage.Headers.Add("slug", escapedSlug);
        return requestMessage;
    }
    
    public static HttpRequestMessage AsArchivalGroup(this HttpRequestMessage requestMessage)
    {
        // This tells Fedora it's an archival group:
        requestMessage.Headers.Add("Link", $"<{RepositoryTypes.ArchivalGroup}>;rel=\"type\"");

        // TODO: Can we now do this in one go and not require the subsequent PATCH? (See AsInsertTypePatch below)
        // But we want to assign an additional type that will be returned in contained resources

        return requestMessage;
    }
    
    public static HttpRequestMessage AsInsertTypePatch(this HttpRequestMessage requestMessage, string type, string callerIdentity)
    {
        var escapedCallerIdentity = callerIdentity.EscapeForLiteralRdf(true);
        var sparql = $$"""
                       PREFIX fedora: <{{RepositoryTypes.FedoraNamespace}}>
                       INSERT {   
                        <> a {{type}} .
                        <> fedora:lastModifiedBy "{{escapedCallerIdentity}}" .
                       }
                       WHERE { }
                       """;

        requestMessage.Content = new StringContent(sparql)
            .WithContentType(SparqlUpdateContentType);
        return requestMessage;
    }    
    
    /// <summary>
    /// Updates a Container's or Archival Group's metadata. A name with text replaces every existing
    /// dc:title (issue #347, decision (a)): one title per resource, with earlier names kept only in
    /// the OCFL versions. Without a name, the existing title is left as it is.
    /// </summary>
    public static HttpRequestMessage WithContainerMetadataUpdate(this HttpRequestMessage requestMessage, string? title, string callerIdentity)
    {
        if (title.HasText())
        {
            return requestMessage.AsReplaceTitlePatch(title, callerIdentity);
        }
        var escapedCallerIdentity = callerIdentity.EscapeForLiteralRdf(true);
        var sparql = $$"""
                       PREFIX fedora: <{{RepositoryTypes.FedoraNamespace}}>
                       INSERT {
                        <> fedora:lastModifiedBy "{{escapedCallerIdentity}}" .
                       }
                       WHERE { }
                       """;

        requestMessage.Content = new StringContent(sparql)
            .WithContentType(SparqlUpdateContentType);
        return requestMessage;
    }

    /// <summary>
    /// Replaces the resource's dc:title with <paramref name="title"/> - a rename (issue #347). Every
    /// existing title is deleted, not just one, so a resource that accumulated several (Archival
    /// Groups did before #347) ends up with exactly one, and a resource with none gets this one.
    /// For a Binary, send this to its fcr:metadata.
    /// </summary>
    /// <remarks>
    /// Two operations, DELETE WHERE then INSERT DATA, not one DELETE/INSERT ... WHERE: Fedora 6.5.1
    /// accepted that single form with WHERE { OPTIONAL { &lt;&gt; dc:title ?title } } but applied only
    /// the INSERT, so the old title stayed beside the new one. Without the OPTIONAL it deleted, but
    /// then a resource with no title matched nothing and got no new title either. This form does
    /// all three cases, inside a transaction too (ReplaceTitleAgainstFedoraTests).
    /// </remarks>
    public static HttpRequestMessage AsReplaceTitlePatch(this HttpRequestMessage requestMessage, string title, string callerIdentity)
    {
        var escapedTitle = title.EscapeForLiteralRdf(true);
        var escapedCallerIdentity = callerIdentity.EscapeForLiteralRdf(true);
        var sparql = $$"""
                       PREFIX dc: <{{RepositoryTypes.DublinCoreElementsNamespace}}>
                       PREFIX fedora: <{{RepositoryTypes.FedoraNamespace}}>
                       DELETE WHERE {
                        <> dc:title ?title .
                       } ;
                       INSERT DATA {
                        <> dc:title "{{escapedTitle}}" .
                        <> fedora:lastModifiedBy "{{escapedCallerIdentity}}" .
                       }
                       """;

        requestMessage.Content = new StringContent(sparql)
            .WithContentType(SparqlUpdateContentType);
        return requestMessage;
    }

    public static HttpRequestMessage AsInsertTitlePatch(this HttpRequestMessage requestMessage,
        string title, string callerIdentity, bool isCreation)
    {
        var escapedTitle = title.EscapeForLiteralRdf(true);
        var escapedCallerIdentity = callerIdentity.EscapeForLiteralRdf(true);
        string creationStatement = isCreation ? $"\r\n <> fedora:createdBy \"{escapedCallerIdentity}\" ." : string.Empty;
        var sparql = $$"""
                       PREFIX dc: <{{RepositoryTypes.DublinCoreElementsNamespace}}>
                       PREFIX fedora: <{{RepositoryTypes.FedoraNamespace}}>
                       INSERT {   
                        <> dc:title "{{escapedTitle}}" .{{creationStatement}}
                        <> fedora:lastModifiedBy "{{escapedCallerIdentity}}" .
                       }
                       WHERE { }
                       """;

        requestMessage.Content = new StringContent(sparql)
            .WithContentType(SparqlUpdateContentType);
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
    
    
    public static HttpRequestMessage WithDigest(this HttpRequestMessage requestMessage, string? digest, string algorithm)
    {
        if (!string.IsNullOrWhiteSpace(digest))
        {
            requestMessage.Headers.Add("digest", $"{algorithm}={digest}");
        }
        return requestMessage;
    }
    
    public static HttpContent WithContentDisposition(this HttpContent httpContent, string? contentDisposition)
    {
        if (!string.IsNullOrWhiteSpace(contentDisposition))
        {
            var encoded = Uri.EscapeDataString(contentDisposition);
            httpContent.Headers.Add("Content-Disposition", $"attachment; filename=\"{encoded}\""); 
        }
        return httpContent;
    }
    
    

    
    public static HttpRequestMessage OverwriteTombstone(this HttpRequestMessage requestMessage)
    {
        requestMessage.Headers.Add("Overwrite-Tombstone", "true");
        return requestMessage;
    }
    
    /// <summary>
    /// This should really obtain the rel=describedBy link via a HEAD request
    /// But in the interests of efficienct, we'll be a little less RESTful and
    /// assume a Fedora convention.
    /// </summary>
    /// <param name="resourceUri"></param>
    /// <returns></returns>
    public static Uri MetadataUri(this Uri resourceUri)
    {
        // I think it's actually impossible to construct the Uri in .NET without dropping back to strings
        // because resourceUri does not have a trailing slash, and the relative Uri would have to be "./fcr:metadata"
        // if you construct that Uri it ends up as https://domain.com/fcr:metadata - the path is stripped.
        return new Uri($"{resourceUri}/fcr:metadata");
    }

    /// <summary>
    /// Same comments as above
    /// </summary>
    /// <param name="resourceUri"></param>
    /// <returns></returns>
    public static Uri VersionsUri(this Uri resourceUri)
    {
        return new Uri($"{resourceUri}/fcr:versions");
    }
    
    
    public static Uri AtVersion(this Uri resourceUri, string? mementoVersion)
    {
        if (mementoVersion.HasText())
        {
            return new Uri($"{resourceUri}/fcr:versions/{mementoVersion}");
        }

        return resourceUri;
    }
    
    
    /// <summary>
    /// Same comments as above
    /// </summary>
    /// <param name="resourceUri"></param>
    /// <returns></returns>
    public static Uri TombstoneUri(this Uri resourceUri)
    {
        return new Uri($"{resourceUri}/fcr:tombstone");
    }
}