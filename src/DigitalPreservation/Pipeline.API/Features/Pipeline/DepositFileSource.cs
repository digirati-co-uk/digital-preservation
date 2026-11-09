using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Storage.Repository.Common;

namespace Pipeline.API.Features.Pipeline;

/// <summary>
/// Where a job reads its deposit's files from. <see cref="ScratchCopy"/> is set when the files were
/// copied for this job and must be deleted afterwards (<see cref="IDepositFileSource.Release"/>).
/// </summary>
public sealed record DepositRoot(string Path, string? ScratchCopy);

/// <summary>
/// Makes a deposit's files available on the local filesystem for the pipeline tools (issue #410).
/// The default working bucket is mounted, so its deposits are read in place; a deposit in a caller's
/// own bucket is copied to scratch disk first, size-capped, and the copy deleted when the job ends.
/// Only reading changes: the pipeline writes its results back through the S3 API either way.
/// </summary>
public interface IDepositFileSource
{
    Task<Result<DepositRoot>> PrepareAsync(Uri? depositFiles, string depositSlug, CancellationToken cancellationToken);

    /// <summary>Deletes a scratch copy; nothing to do for a mounted deposit. Never throws.</summary>
    void Release(DepositRoot? root);
}

public class DepositFileSource(
    ILogger<DepositFileSource> logger,
    IAmazonS3 s3Client,
    IOptions<StorageOptions> storageOptions,
    IOptions<AwsStorageOptions> awsStorageOptions,
    IOptions<PipelineToolOptions> pipelineToolOptions,
    IOptions<PipelineFeatureFlags> featureFlags) : IDepositFileSource
{
    public async Task<Result<DepositRoot>> PrepareAsync(Uri? depositFiles, string depositSlug, CancellationToken cancellationToken)
    {
        if (depositFiles == null || !AmazonS3Uri.TryParseAmazonS3Uri(depositFiles, out var s3Uri))
        {
            return Result.FailNotNull<DepositRoot>(ErrorCodes.BadRequest,
                $"Deposit {depositSlug} has no S3 location for its files.");
        }

        var defaultBucket = awsStorageOptions.Value.DefaultWorkingBucket;
        if (string.IsNullOrEmpty(defaultBucket))
        {
            // Without AwsStorage:DefaultWorkingBucket there is no telling which deposits the mount
            // serves, so read every deposit through it, as before #410. Copying from caller buckets
            // needs the setting.
            if (featureFlags.Value.AllowPipelineForCallerBuckets)
            {
                logger.LogWarning(
                    "AllowPipelineForCallerBuckets is on but AwsStorage:DefaultWorkingBucket is not set; reading deposit {DepositSlug} from the mount",
                    depositSlug);
            }
            return Mounted(depositSlug);
        }

        if (s3Uri.Bucket == defaultBucket)
        {
            return Mounted(depositSlug);
        }

        if (!featureFlags.Value.AllowPipelineForCallerBuckets)
        {
            return Result.FailNotNull<DepositRoot>(ErrorCodes.BadRequest,
                $"Deposit {depositSlug} is in bucket '{s3Uri.Bucket}', which the pipeline cannot reach: " +
                "only the default working bucket is mounted, and copying from other buckets is switched off.");
        }

        return await CopyToScratch(s3Uri.Bucket, s3Uri.Key, depositSlug, cancellationToken);
    }

    private Result<DepositRoot> Mounted(string depositSlug)
    {
        // Unchanged from before #410: the deposit sits at {mount}/{slug}.
        var mountPath = storageOptions.Value.FileMountPath;
        if (string.IsNullOrEmpty(mountPath) || !Directory.Exists(mountPath))
        {
            logger.LogError("S3 mount path could not be found at {MountPath}", mountPath);
            return Result.FailNotNull<DepositRoot>(ErrorCodes.UnknownError, $"S3 mount path could not be found at {mountPath}");
        }
        return Result.OkNotNull(new DepositRoot($"{mountPath}{pipelineToolOptions.Value.DirectorySeparator}{depositSlug}", null));
    }

    private async Task<Result<DepositRoot>> CopyToScratch(string bucket, string? key, string depositSlug, CancellationToken cancellationToken)
    {
        var scratchRoot = storageOptions.Value.ScratchCopyPath;
        if (string.IsNullOrEmpty(scratchRoot))
        {
            return Result.FailNotNull<DepositRoot>(ErrorCodes.UnknownError,
                "Copying deposits from caller buckets is switched on, but StorageOptions:ScratchCopyPath is not set.");
        }

        var prefix = (key ?? string.Empty).TrimStart('/');
        if (prefix.Length > 0 && !prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        var objects = await ListObjects(bucket, prefix, cancellationToken);
        var totalBytes = objects.Sum(o => o.Size ?? 0);
        var maxBytes = storageOptions.Value.ScratchCopyMaxBytes;
        if (totalBytes > maxBytes)
        {
            return Result.FailNotNull<DepositRoot>(ErrorCodes.BadRequest,
                $"Deposit {depositSlug} is {totalBytes:N0} bytes, larger than the {maxBytes:N0} bytes the pipeline " +
                $"will copy from bucket '{bucket}'.");
        }

        var target = Path.Combine(scratchRoot, depositSlug);
        if (!PathX.IsUnderRoot(scratchRoot, target) || Path.GetFullPath(target) == Path.GetFullPath(scratchRoot))
        {
            return Result.FailNotNull<DepositRoot>(ErrorCodes.BadRequest,
                $"Refusing to copy deposit {depositSlug}: its scratch directory would not be inside {scratchRoot}.");
        }

        // A copy left by a job that crashed before its clean-up ran is stale, never reused.
        DeleteIfPresent(target);
        Directory.CreateDirectory(target);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Copying {Count} objects ({Bytes} bytes) of deposit {DepositSlug} from {Bucket} to {Target}",
                objects.Count, totalBytes, depositSlug, bucket, target);
        }
        try
        {
            foreach (var (objectKey, relative) in objects.Select(o => (o.Key, o.Key[prefix.Length..])))
            {
                var destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!PathX.IsUnderRoot(target, destination))
                {
                    throw new InvalidOperationException($"Object key {objectKey} would be copied outside {target}.");
                }
                if (relative.Length == 0 || relative.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                // ChecksumMode makes the SDK verify the object's additional checksum as it reads, where
                // the object was uploaded with one; an object uploaded without one is copied unchecked.
                // Brunnhilde's SHA-256, compared later against the METS, still catches corruption.
                using var response = await s3Client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = objectKey,
                    ChecksumMode = ChecksumMode.ENABLED
                }, cancellationToken);
                await using var file = File.Create(destination);
                await response.ResponseStream.CopyToAsync(file, cancellationToken);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not copy deposit {DepositSlug} from {Bucket} to scratch", depositSlug, bucket);
            DeleteIfPresent(target);
            if (e is OperationCanceledException) throw;
            return Result.FailNotNull<DepositRoot>(ErrorCodes.UnknownError,
                $"Could not copy deposit {depositSlug} from bucket '{bucket}' for processing.");
        }

        return Result.OkNotNull(new DepositRoot(target, target));
    }

    private async Task<List<S3Object>> ListObjects(string bucket, string prefix, CancellationToken cancellationToken)
    {
        var objects = new List<S3Object>();
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await s3Client.ListObjectsV2Async(request, cancellationToken);
            // SDK v4: S3Objects is null rather than empty when nothing matches.
            objects.AddRange(response.S3Objects ?? []);
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
        return objects;
    }

    public void Release(DepositRoot? root)
    {
        if (root?.ScratchCopy == null)
        {
            return;
        }
        var scratchRoot = storageOptions.Value.ScratchCopyPath;
        // Defence in depth, as for the process folders: never delete outside the scratch directory.
        if (string.IsNullOrEmpty(scratchRoot) || !PathX.IsUnderRoot(scratchRoot, root.ScratchCopy)
            || Path.GetFullPath(root.ScratchCopy) == Path.GetFullPath(scratchRoot))
        {
            logger.LogError("Refusing to delete scratch copy {Path}: it is not inside {ScratchRoot}", root.ScratchCopy, scratchRoot);
            return;
        }
        DeleteIfPresent(root.ScratchCopy);
    }

    private void DeleteIfPresent(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Deleted scratch copy {Path}", path);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not delete scratch copy {Path}", path);
        }
    }
}
