using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using Microsoft.Extensions.Options;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

namespace Storage.API.Features.Import.S3;

public class ImportJobResultStore(
    IAmazonS3 s3Client,
    IOptions<AwsStorageOptions> options,
    ILogger<ImportJobResultStore> logger) : IImportJobResultStore
{
    private readonly AwsStorageOptions options = options.Value;
    private readonly string jobResultsPrefix = "importjobresults/";
    
    public async Task<Result> SaveImportJob(string jobIdentifier, ImportJob importJobResult, CancellationToken cancellationToken = default)
    {
        return await Save(jobIdentifier, "-job", importJobResult, cancellationToken);
    }

    public async Task<Result> SaveImportJobResult(string jobIdentifier, ImportJobResult importJobResult, CancellationToken cancellationToken = default)
    {
        return await Save(jobIdentifier, "-result", importJobResult, cancellationToken);
    }
    
    public async Task<Result<ImportJob?>> GetImportJob(string jobIdentifier, CancellationToken cancellationToken)
    {
        return await Load<ImportJob>(jobIdentifier, "-job", cancellationToken);
    }
    
    public async Task<Result<ImportJobResult?>> GetImportJobResult(string jobIdentifier, CancellationToken cancellationToken)
    {
        return await Load<ImportJobResult>(jobIdentifier, "-result", cancellationToken);
    }


    private async Task<Result> Save(string jobIdentifier, string suffix, Resource resource,
        CancellationToken cancellationToken = default)
    {        
        var putReq = new PutObjectRequest
        {
            BucketName = options.DefaultWorkingBucket,
            Key = $"{jobResultsPrefix}{jobIdentifier}-{suffix}",
            ContentType = "application/json",
            ContentBody = JsonSerializer.Serialize(resource),
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256 // might as well
        };
        try
        {
            var resp = await s3Client.PutObjectAsync(putReq, cancellationToken);
            if (resp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                return Result.Ok();
            }
            return ResultHelpers.FailFromAwsStatusCode<object>(resp.HttpStatusCode, "Unable to store Resource", putReq.GetS3Uri());
        }
        catch (AmazonS3Exception s3E)
        {
            return ResultHelpers.FailFromS3Exception<object>(s3E, "Unable to store Resource", putReq.GetS3Uri());
        }
    }

    private async Task<Result<T?>> Load<T>(string jobIdentifier, string suffix, CancellationToken cancellationToken = default) where T : Resource
    {
        var gor = new GetObjectRequest
        {
            BucketName = options.DefaultWorkingBucket,
            Key = $"{jobResultsPrefix}{jobIdentifier}-{suffix}",
        };
        try
        {
            var resp = await s3Client.GetObjectAsync(gor, cancellationToken);
            if (resp.HttpStatusCode == HttpStatusCode.OK)
            {
                var t = await JsonSerializer.DeserializeAsync<T>(
                    resp.ResponseStream,
                    cancellationToken: cancellationToken);
                return Result.Ok(t);
            }
            return ResultHelpers.FailFromAwsStatusCode<T>(
                resp.HttpStatusCode, "Could not read ImportJobResult", gor.GetS3Uri());
        }
        catch (AmazonS3Exception s3E)
        {
            return ResultHelpers.FailFromS3Exception<T>(s3E, "Could not read ImportJobResult", gor.GetS3Uri());
        }
        catch (Exception e)
        {
            return Result.Fail<T?>(ErrorCodes.UnknownError, e.Message);
        }
    }
}