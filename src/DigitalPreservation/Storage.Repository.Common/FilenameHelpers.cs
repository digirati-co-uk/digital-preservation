using System.Text.Encodings.Web;
using DigitalPreservation.Common.Model.Mets;
using Microsoft.Extensions.WebEncoders.Testing;

namespace Storage.Repository.Common;

public static class FilenameHelpers
{
    public static MetsIdentifiers GetIdSafeOperationPath(string operationPath)
    {
        var encodedOperationPath = System.Web.HttpUtility.UrlEncode(operationPath);

        return new MetsIdentifiers
        {
            FileId = $"{Constants.FileIdPrefix}{encodedOperationPath}",
            AdmId = $"{Constants.AdmIdPrefix}{encodedOperationPath}",
            TechId = $"{Constants.TechIdPrefix}{encodedOperationPath}",
            PhysId = $"{Constants.PhysIdPrefix}{encodedOperationPath}"
        };
    }
}
