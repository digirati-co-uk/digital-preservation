using DigitalPreservation.Common.Model.Mets;

namespace Storage.Repository.Common;

public static class FilenameHelpers
{
    public static MetsIdentifiers GetIdSafeOperationPath(string operationPath)
    {
        var encodedOperationPath = EscapeXmlCharactersAndSpaces(operationPath);

        return new MetsIdentifiers
        {
            FileId = $"{Constants.FileIdPrefix}{encodedOperationPath}",
            AdmId = $"{Constants.AdmIdPrefix}{encodedOperationPath}",
            TechId = $"{Constants.TechIdPrefix}{encodedOperationPath}",
            PhysId = $"{Constants.PhysIdPrefix}{encodedOperationPath}"
        };
    }

    public static string EscapeXmlCharactersAndSpaces(string target)
    {
        return
            target
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;")
                .Replace("&amp;amp;", "&amp;")
                .Replace(" ", "+");
    }
}
