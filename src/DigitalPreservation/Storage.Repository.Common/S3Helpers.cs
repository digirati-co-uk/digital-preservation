using Amazon.S3.Model;
using DigitalPreservation.Utils;

namespace Storage.Repository.Common;

public static class S3Helpers
{
    public const string OriginalNameMetadataKey = "Original-Name";
    public const string OriginalNameMetadataResponseKey = "x-amz-meta-original-name";

    /// <summary>
    /// Builds an S3 object key by joining a deposit's root key with every further path segment
    /// (issue #388 item 3.1), via StringUtils.BuildPath - which trims whitespace and stray slashes
    /// from each segment - rather than each caller concatenating raw strings by hand. Diverged:
    /// plain concatenation didn't trim, so a slug with a leading or trailing space could give an
    /// upload and a delete different keys for what a caller would consider the same path (reasoned,
    /// not tested - slug validation may make it unreachable in practice).
    /// </summary>
    public static string BuildDepositObjectKey(string rootKey, params string?[] segments) =>
        StringUtils.BuildPath(false, [rootKey, ..segments]);

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