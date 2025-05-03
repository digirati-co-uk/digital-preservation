using System.Net;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model;

public static class ResourceX
{
    public static string? GetPathUnderRoot(this PreservedResource resource)
    {
        var uri = resource.Id;
        return uri.GetPathUnderRoot();
    }

    public static string? GetPathUnderRoot(this Uri? uri, bool decode = false)
    {
        if(uri == null) return null;
        return GetPathUnderRoot(uri.AbsolutePath, decode);
    }

    public static string? GetPathUnderRoot(this string absolutePath, bool decode = false)
    {
        var pathUnderRoot = absolutePath
            .RemoveStart("/")
            .RemoveStart(PreservedResource.BasePathElement)
            .RemoveStart("/");

        if (decode)
        {
            pathUnderRoot = WebUtility.UrlDecode(pathUnderRoot);
        }
        return pathUnderRoot;
    }

    public static string? GetRepositoryPath(this string? pathUnderRoot)
    {
        if (pathUnderRoot == null)
        {
            return null;
        }
        return StringUtils.BuildPath(true,
            PreservedResource.BasePathElement, pathUnderRoot);
    }
    
    public static string GetDisplayName(this PreservedResource resource)
    {
        return resource.Name ?? resource.Id?.GetSlug()?.UnEscapeFromUri() ?? $"[{resource.GetType()}]";
    }
    
}