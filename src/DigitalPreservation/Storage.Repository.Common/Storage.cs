using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
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
                var wd = new WorkingDirectory { LocalPath = string.Empty, Name = "METSLIKE" };
                var pReqMetsLike = new PutObjectRequest
                {
                    BucketName = options.DefaultWorkingBucket,
                    Key = key + "__METSlike.json",
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
}