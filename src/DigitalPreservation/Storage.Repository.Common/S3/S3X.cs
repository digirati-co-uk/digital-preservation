using Amazon.Runtime.Internal.Util;
using Amazon.S3.Util;
using DigitalPreservation.Utils;

namespace Storage.Repository.Common.S3;

public static class S3X
{
    public static Uri ToUri(this AmazonS3Uri amazonS3Uri)
    {
        return new Uri($"s3://{amazonS3Uri.Bucket}/{amazonS3Uri.Key}");
    }
    
    /// <summary>
    /// This is where the equivalence of S3 keys to URI paths breaks down. They are not equivalent - # is valid in an S3 key but not in a Uri path.
    /// But # is valid in the file system from which the original file came, and is being passed around as a URI.
    /// The slipping in and out of URI / bucket-and-key / filesystem representations of a location leaks... there's a small hole in it (even without any encoding issues)
    /// </summary>
    /// <param name="amazonS3Uri"></param>
    /// <param name="originalUri"></param>
    /// <returns></returns>
    public static string GetKeyFromLocalPath(this AmazonS3Uri amazonS3Uri, Uri? originalUri = null)
    {
        // Use the source Uri to get the key if possible
        // Or unescape the path?
        string? keyPart = null;
        string? keyPartFromLocalPath = null;
        string? keyPartFromUnescapedAbsolutePath = null;
        if (originalUri != null)
        {
            keyPartFromLocalPath = originalUri.LocalPath.RemoveStart("/");
            keyPartFromUnescapedAbsolutePath = originalUri.AbsolutePath.UnEscapeFromUri().RemoveStart("/");
            if (originalUri.OriginalString.HasText())
            {
                var pathStarts = originalUri.AbsoluteUri.IndexOf(originalUri.AbsolutePath, StringComparison.Ordinal);
                keyPart = originalUri.OriginalString.Substring(pathStarts + 1);
            }
        }

        if (keyPart != keyPartFromUnescapedAbsolutePath || keyPart != keyPartFromLocalPath || keyPart != amazonS3Uri.Key)
        {
            Console.WriteLine($"keyPartFromLocalPath is '{keyPartFromLocalPath}', keyPartFromUnescapedAbsolutePath is '{keyPartFromUnescapedAbsolutePath}', keyPart is '{keyPart}', amazonS3Uri.Key is '{amazonS3Uri.Key}'");
        }
        if (keyPartFromLocalPath.HasText())
        {
            return keyPartFromLocalPath;
        }
        return amazonS3Uri.Key;
    }
}