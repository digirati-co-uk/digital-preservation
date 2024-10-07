using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Features.S3;

public class UploadFileToDeposit(Uri s3Root, string? parent, string slug, IFormFile file, string checksum, string depositFileName, string contentType) : IRequest<Result<MovingFile?>>
{
    public Uri S3Root { get; } = s3Root;
    public string? Parent { get; } = parent;
    public string Slug { get; } = slug;
    public IFormFile File { get; } = file;
    public string Checksum { get; } = checksum;
    public string DepositFileName { get; } = depositFileName;
    public string ContentType { get; } = contentType;
}

public class UploadFileToDepositHandler(IAmazonS3 s3Client) : IRequestHandler<UploadFileToDeposit, Result<MovingFile?>>
{
    public async Task<Result<MovingFile?>> Handle(UploadFileToDeposit request, CancellationToken cancellationToken)
    {
        // TODO: Record in METS, use DepositFileName, Checksum

        var s3Uri = new AmazonS3Uri(request.S3Root);
        var fullKey = StringUtils.BuildPath(false, s3Uri.Key, request.Parent, request.Slug);
        PutObjectResponse? response = null;
        var req = new PutObjectRequest
        {
            BucketName = s3Uri.Bucket,
            Key = fullKey,
            ContentType = request.ContentType,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            InputStream = request.File.OpenReadStream()
        };
        try
        {
            response = await s3Client.PutObjectAsync(req, cancellationToken);
            if(response is { ChecksumSHA256: not null }
               && AwsChecksum.FromBase64ToHex(response.ChecksumSHA256) == request.Checksum)
            {
                var file = new MovingFile
                {
                    LocalPath = fullKey.RemoveStart(s3Uri.Key)!,
                    ContentType = request.ContentType,
                    Digest = request.Checksum,
                    Size = response.ContentLength
                };
                return Result.Ok(file);
            }
        }
        catch (AmazonS3Exception s3E)
        {
            switch (s3E.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    return Result.Fail<MovingFile?>(ErrorCodes.Conflict, "Conflicting resource at " + fullKey);
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<MovingFile?>(ErrorCodes.Unauthorized, "Unauthorized for " + fullKey);
                case HttpStatusCode.BadRequest:
                    return Result.Fail<MovingFile?>(ErrorCodes.BadRequest, "Bad Request");
                default:
                    return Result.Fail<MovingFile>(ErrorCodes.UnknownError,
                        $"AWS returned status code {s3E.StatusCode} when trying to create {req.GetS3Uri()}.");
            }
        }
        return Result.Fail<MovingFile>(ErrorCodes.UnknownError, $"Could not upload file to {s3Uri}.");
        
    }
}