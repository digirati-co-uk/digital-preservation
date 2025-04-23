using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public enum Whereabouts
{
    Both,
    Mets,
    Deposit,
    Neither,
    Extra
}

public static class FolderNames
{
    public const string Objects = "objects";
    public const string Metadata = "metadata";
    public const string BagItData = "data";

    public static bool PathIsSpecial(string localPath)
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
}
