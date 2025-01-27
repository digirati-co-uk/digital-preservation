using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.Repository.Common.S3;

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
            var root = RootDirectory();
            if (useObjectTemplate)
            {
                var por = new PutObjectRequest
                {
                    BucketName = options.DefaultWorkingBucket,
                    Key = key + "objects/"
                };
                por.Metadata.Add(S3Helpers.OriginalNameMetadataKey, "objects");
                await s3Client.PutObjectAsync(por);
                root.Directories.Add(new WorkingDirectory
                {
                    LocalPath = "objects",
                    Modified = root.Modified,
                    Name = "objects"
                });
            }
            var metsKey = key + IStorage.MetsLike;
            var writeMetsResult = await WriteMetsLike(options.DefaultWorkingBucket, metsKey, root);
            if (writeMetsResult.Failure)
            {
                return Result.Generify<Uri>(writeMetsResult);
            }
            return Result.OkNotNull(pReq.GetS3Uri());
        }
        var failResult = ResultHelpers.FailFromAwsStatusCode<Uri>(pResp.HttpStatusCode, "Could not create deposit file location.", pReq.GetS3Uri());
        return Result.Generify<Uri>(failResult);
    }

    private async Task<Result> WriteMetsLike(string bucket, string key, WorkingDirectory wd, CancellationToken cancellationToken = default)
    {
        wd.Modified = DateTime.UtcNow;
        // make sure the key itself is in the WorkingDirectory
        var metsLikeInWorkingDirectory = wd.Files.SingleOrDefault(f => f.LocalPath == key.GetSlug());
        if (metsLikeInWorkingDirectory == null)
        {
            wd.Files.Add(new WorkingFile
            {
                LocalPath = key.GetSlug(),
                ContentType = "application/json",
                Name = key.GetSlug(),
                Modified = DateTime.UtcNow
            });
        }
        OrderAlphanumerically(wd);
        var pReqMetsLike = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(wd),
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256
        };
        pReqMetsLike.Metadata.Add(S3Helpers.OriginalNameMetadataKey, key.GetSlug());
        try
        {
            var resp = await s3Client.PutObjectAsync(pReqMetsLike, cancellationToken);
            if (resp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                return Result.Ok();
            }
            return ResultHelpers.FailFromAwsStatusCode<WorkingDirectory>(resp.HttpStatusCode, "Unable to write METSlike.json", pReqMetsLike.GetS3Uri());
        }
        catch (AmazonS3Exception s3E)
        {
            return ResultHelpers.FailFromS3Exception<WorkingDirectory>(s3E, "Unable to write METSlike.json", pReqMetsLike.GetS3Uri());
        }
    }

  
    public static WorkingDirectory RootDirectory()
    {
        return new WorkingDirectory
        {
            LocalPath = string.Empty,
            Name = WorkingDirectory.DefaultRootName,
            Modified = DateTime.UtcNow
        };
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

    public async Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory directoryToAdd, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentResult = await ReadMetsLike(location, metsLikeFilename, cancellationToken);
            if (currentResult.Success)
            {
                var root = currentResult.Value!;
                var newDir = root.FindDirectory(directoryToAdd.LocalPath, true);
                newDir!.Name = directoryToAdd.Name; // should have been created along path
                newDir.Modified = directoryToAdd.Modified;
                var saveResult = await SaveMetsLike(location, metsLikeFilename, root, cancellationToken);
                return saveResult.Success ? Result.OkNotNull(root) : Result.Generify<WorkingDirectory>(saveResult);
            }
            return Result.FailNotNull<WorkingDirectory>(currentResult.ErrorCode!, currentResult.ErrorMessage);
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingDirectory>(s3E, "Could not add directory to METS", location.ToUri());
            return Result.Generify<WorkingDirectory>(exResult);
        }
    }

    public async Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingFile fileToAdd, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentResult = await ReadMetsLike(location, metsLikeFilename, cancellationToken);
            if (currentResult.Success)
            {
                var root = currentResult.Value!;
                var parentDir = root.FindDirectory(fileToAdd.LocalPath.GetParent(), false);
                if (parentDir == null)
                {
                    return Result.FailNotNull<WorkingDirectory>(ErrorCodes.NotFound, "Parent directory does not exist");
                }
                parentDir.Files.Add(fileToAdd);
                var saveResult = await SaveMetsLike(location, metsLikeFilename, root, cancellationToken);
                return saveResult.Success ? Result.OkNotNull(root) : Result.Generify<WorkingDirectory>(saveResult);
            }
            return Result.FailNotNull<WorkingDirectory>(currentResult.ErrorCode!, currentResult.ErrorMessage);
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingDirectory>(s3E, "Could not add file to METS", location.ToUri());
            return Result.Generify<WorkingDirectory>(exResult);
        }
    }
    
    
    private async Task<Result> SaveMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory root,
        CancellationToken cancellationToken = default)
    {
        var key = StringUtils.BuildPath(false, location.Key, metsLikeFilename);
        var saveResult = await WriteMetsLike(location.Bucket, key, root, cancellationToken);
        return saveResult;
    }

    public async Task<Result> DeleteFromMetsLike(AmazonS3Uri location, string metsLikeFilename, string path, CancellationToken cancellationToken = default)
    {
        var wdResult = await ReadMetsLike(location, metsLikeFilename, cancellationToken);
        if (wdResult.Success)
        {
            bool somethingWasRemoved = false;
            var root = wdResult.Value!;
            var dir = root.FindDirectory(path, false);
            if (dir != null)
            {
                // dir is the directory to be deleted
                var parentOfDir = root.FindDirectory(path.GetParent(), false);
                somethingWasRemoved = parentOfDir!.Directories.Remove(dir);
            }
            else
            {
                var parentOfFile = root.FindDirectory(path.GetParent(), false);
                if (parentOfFile != null)
                {
                    var file = parentOfFile.Files.Single(f => f.LocalPath == path);
                    somethingWasRemoved = parentOfFile.Files.Remove(file);
                }
            }
            if (somethingWasRemoved)
            {
                var saveResult = await SaveMetsLike(location, metsLikeFilename, root, cancellationToken);
                return saveResult;
            }
            return Result.Fail(ErrorCodes.NotFound, "Could not delete path from METS: " + path);
        }
        return wdResult;
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
                // OrderAlphanumerically(wd!); Should never have to do this, always saved after calling this
                return Result.Ok(wd);
            }
            var failResult = ResultHelpers.FailFromAwsStatusCode<WorkingDirectory>(
                resp.HttpStatusCode, "Could not read METS location", gor.GetS3Uri());
            return failResult;
            //return Result.Generify<WorkingDirectory?>(failResult);
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingDirectory>(s3E, "Could not read METS location", gor.GetS3Uri());
            return exResult;
            //return Result.Generify<WorkingDirectory?>(exResult);
        }
        catch (Exception e)
        {
            return Result.Fail<WorkingDirectory?>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<WorkingDirectory?>> GenerateMetsLike(AmazonS3Uri location, bool writeToStorage, CancellationToken cancellationToken = default)
    {
        try
        {
            var s3Objects = await ListAllS3Objects(location, cancellationToken);
            var top = RootDirectory();

            // Create the directories
            foreach (var s3Object in s3Objects.OrderBy(o => o.Key.Replace('/', '~')))
            {
                if (s3Object.Key == location.Key)
                {
                    continue; // we don't want the deposit root itself, we already have that in top
                }

                // TODO: If there's any way we can avoid this, by returning the properties we
                // are interested in just from the ListObjects request, then we should do that.
                // But I don't think we can avoid it.
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
                    dir!.Modified = s3Object.LastModified.ToUniversalTime(); // will have been created
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
                        Modified = s3Object.LastModified.ToUniversalTime()
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
                    dir!.Files.Add(wf);
                }
            }

            OrderAlphanumerically(top);
            
            if (writeToStorage)
            {
                var writeResult = await WriteMetsLike(location.Bucket, location.Key + IStorage.MetsLike, top, cancellationToken);
                return writeResult.Success ? Result.Ok(top) : Result.Generify<WorkingDirectory?>(writeResult);
            }
            
            return Result.Ok(top);

        }
        catch (Exception e)
        {
            return Result.Fail<WorkingDirectory>(ErrorCodes.UnknownError, e.Message);
        }
    }

    private async Task<List<S3Object>> ListAllS3Objects(AmazonS3Uri location, CancellationToken cancellationToken)
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

        return s3Objects;
    }

    private void OrderAlphanumerically(WorkingDirectory wd)
    {
        wd.Files.Sort(WorkingBaseComparer);
        wd.Directories.Sort(WorkingBaseComparer);
        foreach (var childWd in wd.Directories)
        {
            OrderAlphanumerically(childWd);
        }
    }

    private int WorkingBaseComparer(WorkingBase x, WorkingBase y)
    {
        return string.Compare(x.LocalPath, y.LocalPath, StringComparison.InvariantCulture);
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


    public async Task<Result<BulkDeleteResult>> EmptyStorageLocation(Uri storageLocation, CancellationToken cancellationToken)
    {
        logger.LogInformation("About to delete contents of {storageLocation}", storageLocation);
        // TODO: MUST Validate bucket and key of root here -
        //  only a permitted bucket, and only a deposits/ path.
        var s3Uri = new AmazonS3Uri(storageLocation);
        var allObjects = await ListAllS3Objects(s3Uri, cancellationToken);

        logger.LogInformation("{locationCount} objects in location {storageLocation}", allObjects.Count, storageLocation);
        int count = 0;
        int deleted = 0;
        while (count < allObjects.Count)
        {
            var batch = allObjects.Skip(count).Take(500).ToList();
            count += batch.Count;
            // note DeleteObjects not DeleteObject
            var dor = new DeleteObjectsRequest
            {
                BucketName = s3Uri.Bucket
            };
            foreach (var s3Obj in batch)
            {
                dor.AddKey(s3Obj.Key);
            }
            logger.LogInformation("Deleting batch of {batchCount} from {storageLocation}", batch.Count, storageLocation);
            var response = await s3Client.DeleteObjectsAsync(dor, cancellationToken);
            logger.LogInformation("AWS reports {deleteCount} objects deleted from {storageLocation}", response.DeletedObjects.Count, storageLocation);
            deleted += response.DeletedObjects.Count;
        }
        logger.LogInformation("Deletion summary for location {storageLocation}", storageLocation);
        logger.LogInformation("Objects to delete: {locationCount}", allObjects.Count);
        logger.LogInformation("Objects processed: {count}", count);
        logger.LogInformation("Objects deleted: {deleted}", deleted);
        var bulkDelete = new BulkDeleteResult
        {
            Location = storageLocation,
            ObjectsToDelete = allObjects.Count,
            ObjectsAttempted = count,
            ObjectsDeleted = deleted
        };
        return Result.OkNotNull(bulkDelete);
    }

    public async Task<Result<ImportSource>> GetImportSource(
        Uri sourceUri,
        CancellationToken cancellationToken)
    { 
        var s3Uri = new AmazonS3Uri(sourceUri);
        WorkingDirectory workingDirectory;
        var readResult = await GenerateMetsLike(s3Uri, true, cancellationToken);
        
        if (readResult is { Success: true, Value: not null })
        {
            workingDirectory = readResult.Value;
        }
        else
        {
            return Result.ConvertFailNotNull<WorkingDirectory?, ImportSource>(readResult);
        }
        
        // We don't embellish from METS here.
        // That's a separate function performed only by the Preservation API.

        var importSource = new ImportSource
        {
            Root = workingDirectory,
            Source = sourceUri
        };
        
        return Result.OkNotNull(importSource);
    }

    public async Task<Result<string?>> GetExpectedDigest(Uri? binaryOrigin, string? binaryDigest)
    {     
        var s3Uri = new AmazonS3Uri(binaryOrigin);
        // Get the SHA256 algorithm from AWS directly rather than compute it here
        // If the S3 file does not already have the SHA-256 in metadata, then it's an error
        var expected = await AwsChecksum.GetHexChecksumAsync(s3Client, s3Uri.Bucket, s3Uri.Key);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return Result.Fail<string>(ErrorCodes.BadRequest, $"S3 Key at {s3Uri} does not have SHA256 Checksum in its attributes");
        }

        // This would be an efficient way of doing this - but with this naive implementation
        // we're going to read the object twice
        // var s3Stream = await s3Client!.GetObjectStreamAsync(s3Uri.Bucket, s3Uri.Key, null);
        // expected = Checksum.Sha256FromStream(s3Stream);
        // could get a byte array here and then pass it along eventually to MakeBinaryPutOrPost
        // for now just read it twice.
        // Later we'll get the sha256 checksum from metadata
        // Or the MD5 from eTag?
        // BEWARE that multipart uploads will not have the MD5 as the eTag.
                    
        // validation
        if (!string.IsNullOrWhiteSpace(binaryDigest) && binaryDigest != expected)
        {
            return Result.Fail<string>(ErrorCodes.BadRequest, $"S3 Key at {s3Uri} does not match provided checksum");
        }
        return Result.Ok(expected);
    }

    public async Task<byte[]> GetBytes(Uri binaryOrigin)
    {
        var s3Uri = new AmazonS3Uri(binaryOrigin);
        var s3Req = new GetObjectRequest
        {
            BucketName = s3Uri.Bucket, Key = s3Uri.Key
        };
        var ms = new MemoryStream();
        var s3Resp = await s3Client!.GetObjectAsync(s3Req);
        await s3Resp.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }
}