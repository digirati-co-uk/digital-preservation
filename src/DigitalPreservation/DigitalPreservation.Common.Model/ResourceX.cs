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
        return GetPathUnderRoot(uri.AbsolutePath);
    }

    public static string? GetPathUnderRoot(this string absolutePath)
    {
        var pathUnderRoot = absolutePath
            .RemoveStart("/")
            .RemoveStart(PreservedResource.BasePathElement)
            .RemoveStart("/");
        
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
        return resource.Name ?? resource.Id?.GetSlug() ?? $"[{resource.GetType()}]";
    }
    
}