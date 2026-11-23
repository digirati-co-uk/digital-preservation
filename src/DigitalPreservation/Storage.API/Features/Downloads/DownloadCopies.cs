using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Download;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Storage.API.Fedora;
using Storage.Repository.Common;

namespace Storage.API.Features.Downloads;

/// <summary>
/// The copies of preserved files that people have asked to download. Each is a copy of one file,
/// in the downloads bucket under its request's id: <c>{id}/{file name}</c>. Nothing is ever handed
/// out from the repository's own bucket.
/// </summary>
public class DownloadCopies(
    IAmazonS3 s3Client,
    IStorageMapper storageMapper,
    IOptions<DownloadOptions> options,
    ILogger<DownloadCopies> logger)
{
    private string Bucket => options.Value.Bucket!;

    public bool HasBucket => options.Value.Bucket.HasText();

    /// <summary>Finds the file in the repository, without copying anything.</summary>
    public async Task<Result<DownloadCopyJob>> Locate(DownloadCopy copy)
    {
        if (copy.Id.Length == 0 || !copy.Id.All(char.IsAsciiLetterOrDigit))
        {
            return Result.FailNotNull<DownloadCopyJob>(ErrorCodes.BadRequest, "The id must be letters and digits only.");
        }
        if (copy.Name is "" or "." or ".." || copy.Name.Contains('/') || copy.Name.Contains('\\'))
        {
            return Result.FailNotNull<DownloadCopyJob>(ErrorCodes.BadRequest, "The name must be a file name, not a path.");
        }

        StorageMap storageMap;
        try
        {
            storageMap = await storageMapper.GetStorageMap(copy.ArchivalGroup);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read the storage map for {ArchivalGroup}", copy.ArchivalGroup);
            return Result.FailNotNull<DownloadCopyJob>(ErrorCodes.NotFound, $"No Archival Group at {copy.ArchivalGroup}");
        }

        if (storageMap.Hashes.TryGetValue(copy.Digest, out var pathInObject))
        {
            return Result.OkNotNull(new DownloadCopyJob(copy, storageMap.Root, $"{storageMap.ObjectPath}/{pathInObject}"));
        }
        return Result.FailNotNull<DownloadCopyJob>(ErrorCodes.NotFound,
            $"No file with digest {copy.Digest} in the current version of {copy.ArchivalGroup}");
    }

    /// <summary>
    /// Copies the file, then checks the SHA-256 that S3 computed for the copy against the file's
    /// digest. A copy that does not match is removed, so a copy that is there is a good one.
    /// </summary>
    public async Task Copy(DownloadCopyJob job, CancellationToken cancellationToken)
    {
        var copy = job.Copy;
        var request = new CopyObjectRequest
        {
            SourceBucket = job.SourceBucket,
            SourceKey = job.SourceKey,
            DestinationBucket = Bucket,
            DestinationKey = $"{copy.Id}/{copy.Name}",
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            MetadataDirective = S3MetadataDirective.REPLACE,
            ContentType = copy.ContentType ?? "application/octet-stream"
        };
        var response = await s3Client.CopyObjectAsync(request, cancellationToken);
        var copiedDigest = AwsChecksum.FromBase64ToHex(response.ChecksumSHA256);
        if (copiedDigest == copy.Digest)
        {
            logger.LogInformation("Copied {Digest} for download {Id}", copy.Digest, copy.Id);
            return;
        }

        logger.LogError("Removing the copy for download {Id}: its digest is {CopiedDigest}, not {Digest}",
            copy.Id, copiedDigest, copy.Digest);
        await Delete(copy.Id, cancellationToken);
    }

    public async Task<bool> IsReady(string id, CancellationToken cancellationToken) =>
        await FindKey(id, cancellationToken) is not null;

    /// <summary>A signed link to the copy, or null if there is no copy.</summary>
    public async Task<DownloadLink?> GetLink(string id, CancellationToken cancellationToken)
    {
        var key = await FindKey(id, cancellationToken);
        if (key is null)
        {
            return null;
        }

        var attachment = new ContentDispositionHeaderValue("attachment");
        attachment.SetHttpFileName(key[(id.Length + 1)..]);
        var expires = DateTime.UtcNow.AddSeconds(options.Value.LinkLifetimeSeconds);
        var url = await s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expires,
            ResponseHeaderOverrides = { ContentDisposition = attachment.ToString() }
        });
        return new DownloadLink { Url = new Uri(url), Expires = expires };
    }

    public async Task Delete(string id, CancellationToken cancellationToken)
    {
        var key = await FindKey(id, cancellationToken);
        if (key is not null)
        {
            await s3Client.DeleteObjectAsync(Bucket, key, cancellationToken);
        }
    }

    private async Task<string?> FindKey(string id, CancellationToken cancellationToken)
    {
        var listing = await s3Client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = Bucket, Prefix = id + "/", MaxKeys = 1 }, cancellationToken);
        return listing.S3Objects?.FirstOrDefault()?.Key;
    }
}
