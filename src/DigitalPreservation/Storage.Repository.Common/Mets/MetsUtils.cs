using DigitalPreservation.Utils;

namespace Storage.Repository.Common.Mets;

public static class MetsUtils
{
    public static bool IsMetsFile(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return name.EndsWith(".xml") && name.Contains("mets");
    }
    
    public static (Uri root, Uri? file) GetRootAndFile(Uri metsLocation)
    {
        // If metsLocation ends with .xml, it's assumed to be the METS file itself.
        // If not, it's assumed to be its containing directory / key.
        // No other possibilities are supported.
        Uri root;
        Uri? file = null;
        var slug = metsLocation.GetSlug();
        if (slug.HasText() && IsMetsFile(slug))
        {
            file = metsLocation;
            root = metsLocation.GetParentUri(trimTrailingSlash:false)!;
        }
        else
        {
            if (metsLocation.AbsoluteUri.EndsWith("/"))
            {
                root = metsLocation;
            }
            else
            {
                root = new Uri(metsLocation.AbsoluteUri + "/");
            }
        }

        return (root, file);
    }
}