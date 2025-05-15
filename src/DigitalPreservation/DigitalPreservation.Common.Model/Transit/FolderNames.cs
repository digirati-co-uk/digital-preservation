using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public static class FolderNames
{
    public const string Objects = "objects";
    public const string Metadata = "metadata";
    public const string BagItData = "data";

    public static bool PathIsKnownFirstLevelDirectory(string localPath)
    {
        return localPath is Objects or Metadata;
    }

    public static string GetPathPrefix(bool isBagItLayout)
    {
        return isBagItLayout ? $"{BagItData}/" : string.Empty;
    }

    public static string? RemovePathPrefix(string? path)
    {
        return path?.RemoveStart($"{BagItData}/");
    }

    public static Uri GetFilesLocation(Uri actualRootOrigin, bool isBagItLayout)
    {
        if (!isBagItLayout) return actualRootOrigin;
        
        var dataSlug = BagItData + (actualRootOrigin.ToString().EndsWith('/') ? "/" : "");
        return actualRootOrigin.AppendEscapedSlug(dataSlug);
    }

    public static bool IsMetadata(string localPath)
    {
        return localPath.StartsWith($"{BagItData}/{Metadata}/") || localPath.StartsWith($"{Metadata}/");
    }
}
