using Amazon.S3.Model;
using Amazon.S3.Util;

namespace Storage.Repository.Common.S3;

public static class S3X
{
    public static System.Uri ToUri(this AmazonS3Uri amazonS3Uri)
    {
        return new Uri($"s3://{amazonS3Uri.Bucket}/{amazonS3Uri.Key}");
    }
}