using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model;

public static class ResourceX
{
    public static string? GetPathUnderRoot(this PreservedResource resource)
    {
        var uri = resource.Id;
        return uri.GetPathUnderRoot();
    }

    public static string? GetPathUnderRoot(this Uri? uri)
    {
        if(uri == null) return null;

        var pathUnderRoot = uri.AbsolutePath
            .RemoveStart("/")
            .RemoveStart(PreservedResource.BasePathElement)
            .RemoveStart("/");
        
        return pathUnderRoot;
    }

    public static string GetRepositoryPath(this string? pathUnderRoot)
    {
        return StringUtils.BuildPath(true,
            PreservedResource.BasePathElement, pathUnderRoot);
    }
    
    public static string GetDisplayName(this PreservedResource resource)
    {
        return resource.Name ?? resource.Id?.GetSlug() ?? $"[{resource.GetType()}]";
    }
    
}