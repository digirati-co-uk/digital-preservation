using System.Net;
using Amazon.S3.Model;
using DigitalPreservation.Utils;

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

    /// <summary>
    /// Builds an S3 object key by joining a deposit's root key with every further path segment
    /// (issue #388 item 3.1), rather than each caller concatenating raw strings by hand.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <see cref="StringUtils.BuildPath"/>, despite the overlap: that trims
    /// whitespace from each segment, which is right for a UI-facing or repository path but wrong
    /// here. An S3 key has to address whatever object is actually sitting in the bucket, and a file
    /// written straight to S3 - bypassing the platform's own slug validation - can genuinely have a
    /// leading or trailing space in its name. Trimming the key built for a delete of such a file
    /// would address a different key to the one it was uploaded under, so the delete either throws
    /// (NoSuchKey, if the request is strict about it) or silently does nothing (S3's DeleteObject is
    /// idempotent and reports success for a key that was never there). Only stray/empty path
    /// segments - e.g. a trailing slash on the root key - are collapsed; every character inside a
    /// real segment, whitespace included, is preserved exactly.
    /// </remarks>
    public static string BuildDepositObjectKey(string rootKey, params string?[] segments) =>
        string.Join('/', new[] { rootKey }.Concat(segments)
            .Where(s => s.HasText())
            .SelectMany(s => s!.Split('/', StringSplitOptions.RemoveEmptyEntries)));

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