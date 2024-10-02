using Amazon.S3.Model;

namespace Storage.Repository.Common;

public static class S3Helpers
{
    public static Uri GetS3Uri(this PutObjectRequest putObjectRequest) =>
        new UriBuilder($"s3://{putObjectRequest.BucketName}") { Path = putObjectRequest.Key }.Uri;
}