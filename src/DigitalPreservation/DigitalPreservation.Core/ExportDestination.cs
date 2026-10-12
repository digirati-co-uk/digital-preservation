using System.Diagnostics.CodeAnalysis;
using Amazon.S3.Util;

namespace DigitalPreservation.Core;

/// <summary>
/// Whether an export's destination is one of the platform's own deposit buckets - shared by
/// Preservation API and Storage API so the parsing and the wording can't drift (issue #288, decision
/// 7 / N11). The Storage API's export endpoints otherwise accepted any S3 destination and copied an
/// Archival Group's files there, relying only on the service role's IAM policy and the private
/// network.
/// </summary>
public static class ExportDestination
{
    /// <summary>
    /// True when <paramref name="destination"/> parses as an S3 location (the same parser
    /// <c>ExecuteExport</c> uses, so a destination is judged by the bucket S3 will actually write
    /// to) whose bucket equals one of <paramref name="permittedBuckets"/> (ordinal comparison - S3
    /// bucket names are lower-case). An unparseable destination is refused. Bucket-level only; a
    /// prefix within an otherwise-permitted bucket is not checked.
    /// </summary>
    public static bool IsInBucket(
        Uri destination, IEnumerable<string> permittedBuckets, [NotNullWhen(false)] out string? reason)
    {
        if (!AmazonS3Uri.TryParseAmazonS3Uri(destination, out var s3Uri))
        {
            reason = $"Export destination '{destination}' is not a valid S3 location.";
            return false;
        }

        if (!permittedBuckets.Contains(s3Uri.Bucket, StringComparer.Ordinal))
        {
            // Names the bucket that was refused, not the whole permitted list - the caller can see
            // what it sent, but the set of deposit buckets isn't advertised to callers. Log the
            // permitted list at the call site instead, so a misconfiguration is diagnosable.
            reason = $"Export destination bucket '{s3Uri.Bucket}' is not a deposit bucket.";
            return false;
        }

        reason = null;
        return true;
    }
}
