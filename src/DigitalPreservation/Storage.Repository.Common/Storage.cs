using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Storage.Repository.Common;

public class Storage : IStorage
{
    private readonly AwsStorageOptions options;
    private readonly IAmazonS3 s3Client;
    private readonly ILogger<Storage> logger;
    
    public Storage(IAmazonS3 s3Client,
        IOptions<AwsStorageOptions> options,
        ILogger<Storage> logger)
    {
        this.s3Client = s3Client;
        this.logger = logger;
        this.options = options.Value;
    }
    
    public async Task<bool> CanSeeStorage()
    {
        GetObjectResponse resp;
        try
        {
            var req = new GetObjectRequest
            {
                BucketName = options.DefaultWorkingBucket,
                Key = options.S3HealthCheckKey
            };
            try
            {
                resp = await s3Client.GetObjectAsync(req);
            }
            catch (AmazonS3Exception s3e)
            {
                if (s3e.StatusCode == HttpStatusCode.NotFound)
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
                        return true;
                    }
                    logger.LogWarning("S3 check returned status {status} on PUT", pResp.HttpStatusCode);
                    return false;
                }
                throw;
            }

            if (resp.HttpStatusCode == HttpStatusCode.OK)
            {
                logger.LogDebug("S3 check can read S3 bucket");
                return true;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "S3 check failed");
        }

        return false;
    }
}