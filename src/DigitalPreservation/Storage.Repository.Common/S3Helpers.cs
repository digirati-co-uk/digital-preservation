using System.Net;
using Amazon.S3.Model;

namespace Storage.Repository.Common;

public static class S3Helpers
{
    public const string OriginalNameMetadataKey = "Original-Name";
    public const string OriginalNameMetadataResponseKey = "x-amz-meta-original-name";

    /// <summary>
    /// Writes the original-name S3 metadata, URL-encoded (issue #388 item 3.3). Two of the four
    /// writers used to write it raw instead, safe today only because they write names the platform
    /// chose (a managed deposit's folder name, or the deposit-file-system JSON's own file name) -
    /// never anything from a caller - but every reader (GetOriginalName) always UrlDecodes, so a
    /// raw write and an encoded write were never actually interchangeable. One writer closes that.
    /// </summary>
    public static void SetOriginalName(this MetadataCollection metadata, string name) =>
        metadata.Add(OriginalNameMetadataKey, WebUtility.UrlEncode(name));

    /// <summary>The original-name S3 metadata, UrlDecoded, or null if the object doesn't carry one.</summary>
    public static string? GetOriginalName(this MetadataCollection metadata) =>
        metadata.Keys.Contains(OriginalNameMetadataResponseKey)
            ? WebUtility.UrlDecode(metadata[OriginalNameMetadataResponseKey])
            : null;

    public static Uri GetS3Uri(this S3Object s3Object) =>
        new UriBuilder($"s3://{s3Object.BucketName}") { Path = s3Object.Key }.Uri;
    
    public static Uri S3UriInBucket(this string key, string bucket) =>
        new UriBuilder($"s3://{bucket}") { Path = key }.Uri;
    
    public static Uri GetS3Uri(this PutObjectRequest putObjectRequest) =>
        new UriBuilder($"s3://{putObjectRequest.BucketName}") { Path = putObjectRequest.Key }.Uri;
    
    public static Uri GetS3Uri(this GetObjectRequest getObjectRequest) =>
        new UriBuilder($"s3://{getObjectRequest.BucketName}") { Path = getObjectRequest.Key }.Uri;
    
    public static Uri GetS3Uri(this DeleteObjectRequest deleteObjectRequest) =>
        new UriBuilder($"s3://{deleteObjectRequest.BucketName}") { Path = deleteObjectRequest.Key }.Uri;
    
    public static Uri GetSourceS3Uri(this CopyObjectRequest copyObjectRequest) =>
        new UriBuilder($"s3://{copyObjectRequest.SourceBucket}") { Path = copyObjectRequest.SourceKey }.Uri;
    
    public static Uri GetDestinationS3Uri(this CopyObjectRequest copyObjectRequest) =>
        new UriBuilder($"s3://{copyObjectRequest.DestinationBucket}") { Path = copyObjectRequest.DestinationKey }.Uri;
}