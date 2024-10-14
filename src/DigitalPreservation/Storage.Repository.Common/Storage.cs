using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Storage.Repository.Common;

public class Storage(
    IAmazonS3 s3Client,
    IOptions<AwsStorageOptions> options,
    ILogger<Storage> logger) : IStorage
{
    private readonly AwsStorageOptions options = options.Value;

    public async Task<Result<Uri>> GetWorkingFilesLocation(string idPart, bool useObjectTemplate, string? callerIdentity = null)
    {
        // This will be able to yield different locations in different buckets for different callers
        // e.g., Goobi
        var key = "deposits/" + idPart + "/";
        if (await Exists(key))
        {
            return Result.FailNotNull<Uri>(ErrorCodes.Conflict, $"The deposit file location for {idPart} already exists.");
        }
        var pReq = new PutObjectRequest
        {
            BucketName = options.DefaultWorkingBucket,
            Key = key
        };
        var pResp = await s3Client.PutObjectAsync(pReq);
        if (pResp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
        {
            if (useObjectTemplate)
            {
                var pReqObjects = new PutObjectRequest
                {
                    BucketName = options.DefaultWorkingBucket,
                    Key = key + "objects/"
                };
                await s3Client.PutObjectAsync(pReqObjects);
                var wd = RootDirectory();
                var pReqMetsLike = new PutObjectRequest
                {
                    BucketName = options.DefaultWorkingBucket,
                    Key = key + IStorage.MetsLike,
                    ContentType = "application/json",
                    ContentBody = JsonSerializer.Serialize(wd),
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                await s3Client.PutObjectAsync(pReqMetsLike);
            }
            return Result.OkNotNull(pReq.GetS3Uri());
        }
        return Result.FailNotNull<Uri>(ErrorCodes.UnknownError, $"AWS returned status code {pResp.HttpStatusCode} when trying to create {pReq.GetS3Uri()}.");
    }

    private static WorkingDirectory RootDirectory()
    {
        return new WorkingDirectory { LocalPath = string.Empty, Name = "__ROOT" };
    }


    private GetObjectRequest MakeGetRequest(string key, string? bucket = null)
    {
        return new GetObjectRequest
        {
            BucketName = bucket ?? options.DefaultWorkingBucket,
            Key = key
        };
    }

    private async Task<bool> Exists(string key, string? bucket = null)
    {
        var req = MakeGetRequest(key, bucket);
        try
        {
            var resp = await s3Client.GetObjectAsync(req);
            if (resp.HttpStatusCode == HttpStatusCode.OK)
            {
                return true;
            }
            throw new InvalidOperationException("AWS returned status code: " + resp.HttpStatusCode + " from GetObjectRequest");
        }
        catch (AmazonS3Exception s3E)
        {
            if (s3E.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            throw;
        }
    }

    public async Task<ConnectivityCheckResult> CanSeeStorage(string source)
    {
        var result = new ConnectivityCheckResult { Name = source, Success = false };
        try
        {
            var req = MakeGetRequest(options.S3HealthCheckKey!);
            GetObjectResponse resp;
            try
            {
                resp = await s3Client.GetObjectAsync(req);
            }
            catch (AmazonS3Exception s3E)
            {
                if (s3E.StatusCode == HttpStatusCode.NotFound)
                {
                    var pReq = new PutObjectRequest
                    {
                        BucketName = options.DefaultWorkingBucket,
                        Key = options.S3HealthCheckKey,
                        ContentBody = """{"test": "value"}""",
                        ContentType = "application/json"
                    };
                    var pResp = await s3Client.PutObjectAsync(pReq);
                    if (pResp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
                    {
                        logger.LogDebug("S3 check can write to S3 bucket");
                        result.Success = true;
                        return result;
                    }
                    logger.LogWarning("S3 check returned status {status} on PUT", pResp.HttpStatusCode);
                    result.Error = $"S3 check returned status ${pResp.HttpStatusCode} on PUT";
                    return result;
                }
                throw;
            }

            if (resp.HttpStatusCode == HttpStatusCode.OK)
            {
                logger.LogDebug("S3 check can read S3 bucket");
                result.Success = true;
                return result;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "S3 check failed");
            result.Error = e.Message;
        }

        return result;
    }

    public async Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory directoryToAdd, CancellationToken cancellationToken = default)
    {
        // TODO - error handling
        var currentResult = await ReadMetsLike(location, metsLikeFilename, cancellationToken);
        if (currentResult.Success)
        {
            var root = currentResult.Value!;
            var newDir = root.FindDirectory(directoryToAdd.LocalPath, true);
            newDir.Name = directoryToAdd.Name;
            var saveResult = await SaveMetsLike(location, metsLikeFilename, root, cancellationToken);
            if (saveResult.Success)
            {
                return Result.OkNotNull(root);
            }
        }
        return Result.FailNotNull<WorkingDirectory>(currentResult.ErrorCode!, currentResult.ErrorMessage);
    }

    private async Task<Result> SaveMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory root,
        CancellationToken cancellationToken = default)
    {
        var por = new PutObjectRequest
        {
            BucketName = location.Bucket,
            Key = StringUtils.BuildPath(false, location.Key, metsLikeFilename),
            ContentBody = JsonSerializer.Serialize(root),
            ContentType = "application/json"
        };
        var resp = await s3Client.PutObjectAsync(por, cancellationToken);
        if (resp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
        {
            return Result.Ok();
        }
        // TODO - general AWS responses to error codes
        return Result.Fail(ErrorCodes.UnknownError, "AWS returned status code: " + resp.HttpStatusCode);
    }

    public async Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingFile fileToAdd, CancellationToken cancellationToken = default)
    {
        // TODO - error handling
        var currentResult = await ReadMetsLike(location, metsLikeFilename, cancellationToken);
        if (currentResult.Success)
        {
            var root = currentResult.Value!;
            var parentDir = root.FindDirectory(fileToAdd.LocalPath.GetParent(), false);
            // TODO - check for conflicts! Or overwrite? Merge? contains..localpath
            parentDir.Files.Add(fileToAdd);
            var saveResult = await SaveMetsLike(location, metsLikeFilename, root, cancellationToken);
            if (saveResult.Success)
            {
                return Result.OkNotNull(root);
            }
        }
        return Result.FailNotNull<WorkingDirectory>(currentResult.ErrorCode!, currentResult.ErrorMessage);
    }

    public Task<Result<WorkingDirectory>> DeleteFromMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory directoryToDelete, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<WorkingDirectory>> DeleteFromMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingFile fileToDelete, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<WorkingDirectory?>> ReadMetsLike(AmazonS3Uri location, string metsLikeFilename, CancellationToken cancellationToken)
    {
        var gor = new GetObjectRequest
        {
            BucketName = location.Bucket,
            Key = StringUtils.BuildPath(false, location.Key, metsLikeFilename)
        };
        try
        {
            var resp = await s3Client.GetObjectAsync(gor, cancellationToken);
            if (resp.HttpStatusCode == HttpStatusCode.OK)
            {
                var wd = await JsonSerializer.DeserializeAsync<WorkingDirectory>(
                    resp.ResponseStream,
                    cancellationToken: cancellationToken);
                return Result.Ok(wd);
            }
            return Result.Fail<WorkingDirectory?>(ErrorCodes.UnknownError, 
                $"AWS returned status code {resp.HttpStatusCode} when trying to read {metsLikeFilename}.");
        }
        catch (AmazonS3Exception s3E)
        {
            switch (s3E.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<WorkingDirectory?>(ErrorCodes.Unauthorized, "Unauthorized for " + gor.GetS3Uri());
                case HttpStatusCode.BadRequest:
                    return Result.Fail<WorkingDirectory?>(ErrorCodes.BadRequest, "Bad Request");
                default:
                    return Result.Fail<WorkingDirectory>(ErrorCodes.UnknownError,
                        $"AWS returned status code {s3E.StatusCode} when trying to create {gor.GetS3Uri()}.");
            }
        }
        catch (Exception e)
        {
            return Result.Fail<WorkingDirectory?>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<WorkingDirectory?>> GenerateMetsLike(AmazonS3Uri location, CancellationToken cancellationToken = default)
    {
        try
        {
            var listReq = new ListObjectsV2Request
            {
                BucketName = location.Bucket,
                Prefix = location.Key
            };
            List<S3Object> s3Objects = [];
            var resp = await s3Client.ListObjectsV2Async(listReq, cancellationToken);
            s3Objects.AddRange(resp.S3Objects);
            while (resp.IsTruncated)
            {
                listReq.ContinuationToken = resp.NextContinuationToken;
                resp = await s3Client.ListObjectsV2Async(listReq, cancellationToken);
                s3Objects.AddRange(resp.S3Objects);
            }

            var top = RootDirectory();

            // Create the directories
            foreach (var s3Object in s3Objects.OrderBy(o => o.Key.Replace('/', '~')))
            {
                if (s3Object.Key == location.Key)
                {
                    continue; // we don't want the deposit root itself, we already have that in top
                }

                var gomReq = new GetObjectMetadataRequest
                {
                    BucketName = location.Bucket,
                    Key = s3Object.Key,
                    ChecksumMode = ChecksumMode.ENABLED
                };
                var metadataResponse = await s3Client.GetObjectMetadataAsync(gomReq, cancellationToken);
                var metadata = metadataResponse.Metadata;
                
                var path = s3Object.Key.RemoveStart(location.Key)!;
                if (path.EndsWith('/'))
                {
                    var dir = top.FindDirectory(path, true);
                    dir.Modified = s3Object.LastModified;
                    if (metadata == null) continue;
                    if (metadata.Keys.Contains(S3Helpers.OriginalNameMetadataResponseKey))
                    {
                        dir.Name = metadata[S3Helpers.OriginalNameMetadataResponseKey];
                    }
                }
                else
                {
                    // a file
                    var dir = top.FindDirectory(path.GetParent(), true);
                    var wf = new WorkingFile
                    {
                        LocalPath = path,
                        ContentType = "?",
                        Size = s3Object.Size,
                        Modified = s3Object.LastModified
                    };
                    // need to get these from METS-like
                    wf.ContentType = metadataResponse.Headers.ContentType;
                    wf.Digest = AwsChecksum.FromBase64ToHex(metadataResponse.ChecksumSHA256);
                    if (metadata != null)
                    {
                        if (metadata.Keys.Contains(S3Helpers.OriginalNameMetadataResponseKey))
                        {
                            wf.Name = metadata[S3Helpers.OriginalNameMetadataResponseKey];
                        }
                    }
                    dir.Files.Add(wf);
                }
            }

            return Result.Ok(top);

        }
        catch (Exception e)
        {
            return Result.Fail<WorkingDirectory>(ErrorCodes.UnknownError, e.Message);
        }
    }
}