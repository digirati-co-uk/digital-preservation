namespace Storage.Repository.Common.Mets;

public static class MetsUtils
{
    public static bool IsMetsFile(string fileName, bool mustBeStandardName = false)
    {
        var name = fileName.ToLowerInvariant();
        if (mustBeStandardName)
        {
            return name == "mets.xml";
        }
        return name.EndsWith(".xml") && name.Contains("mets");
    }
}