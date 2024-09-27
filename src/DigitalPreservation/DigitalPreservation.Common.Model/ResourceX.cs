using System.Runtime.CompilerServices;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model;

public static class ResourceX
{
    public static string? GetPathUnderRoot(this PreservedResource resource)
    {
        if(resource.Id == null) return null;

        var pathUnderRoot = resource.Id.AbsolutePath
            .RemoveStart("/")
            .RemoveStart(PreservedResource.BasePathElement)
            .RemoveStart("/");
        
        return pathUnderRoot;
    }

    public static string GetDisplayName(this PreservedResource resource)
    {
        return resource.Name ?? resource.Id?.GetSlug() ?? $"[{resource.GetType()}]";
    }
    
}