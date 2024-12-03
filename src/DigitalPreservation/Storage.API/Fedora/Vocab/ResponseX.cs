using System.Net;

namespace Storage.API.Fedora.Vocab;

public static class ResponseX
{
    public static bool HasArchivalGroupTypeHeader(this HttpResponseMessage response)
    {
        return response.HasLinkTypeHeader(RepositoryTypes.ArchivalGroup);
    }
    public static bool HasBasicContainerTypeHeader(this HttpResponseMessage response)
    {
        return response.HasLinkTypeHeader(RepositoryTypes.BasicContainer);
    }

    public static bool HasBinaryTypeHeader(this HttpResponseMessage response)
    {
        return response.HasLinkTypeHeader(RepositoryTypes.NonRDFSource);
    }

    public static bool IsTombstone(this HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.Gone;
    }

    private static bool HasLinkTypeHeader(this HttpResponseMessage response, string typeId)
    {
        // "Link", $"<{RepositoryTypes.ArchivalGroup}>;rel=\"type\""
        // This could be nicer
        if (response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
        {
            if (values.Any(v => v.Contains(typeId) && v.EndsWith("rel=\"type\"")))
            {
                return true;
            }
        }
        return false;
    }
}