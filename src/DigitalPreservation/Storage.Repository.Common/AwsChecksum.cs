using Amazon.S3;
using Amazon.S3.Model;

namespace Storage.Repository.Common;

public class AwsChecksum
{
    /// <summary>
    /// Read the SHA256 Checksum from the S3 Object's metadata
    /// We want to use hexadecimal representations so we convert from the base64 returned by AWS
    /// </summary>
    /// <param name="s3Client"></param>
    /// <param name="bucket"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static async Task<string?> GetHexChecksumAsync(IAmazonS3 s3Client, string bucket, string key)
    {
        var objAttrsRequest = new GetObjectAttributesRequest()
        {
            BucketName = bucket,
            Key = key,
            ObjectAttributes = [ObjectAttributes.Checksum]
        };
        var objAttrsResponse = await s3Client!.GetObjectAttributesAsync(objAttrsRequest);
        string? base64Sha256 = objAttrsResponse?.Checksum?.ChecksumSHA256;
        return FromBase64ToHex(base64Sha256);
    }

    public static string? FromBase64ToHex(string? base64Sha256)
    {
        if (!string.IsNullOrWhiteSpace(base64Sha256))
        {
            byte[] bytes = Convert.FromBase64String(base64Sha256);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        return null;
    }
}