using System.Security.Cryptography;
using System.Text;

namespace Storage.API.Fedora;

public static class RepositoryPath
{
    // id="foo/bar"; id_digest=$(echo -n "info:fedora/${id}" | sha256sum -); echo "${id_digest:0:3}/${id_digest:3:3}/${id_digest:6:3}/${id_digest}"

    public static string RelativeToRoot(string ocflS3Prefix, string id, int numberOfTuples = 3, int tupleSize = 3)
    {
        var infoId = $"info:fedora/{id}";
        var bytes = Encoding.UTF8.GetBytes(infoId);
        var hash = SHA256.HashData(bytes);
        var idDigest = Convert.ToHexString(hash).ToLower();
        var sb = new StringBuilder();
        for (int i = 0; i < numberOfTuples; i++)
        {
            sb.Append(idDigest.Substring(i*tupleSize, tupleSize));
            sb.Append("/");
        }
        sb.Append(idDigest);
        return ocflS3Prefix + sb.ToString();
    }

}