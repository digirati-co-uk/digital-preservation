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

public class UploadFileToDeposit(Uri s3Root, string? parent, string slug, IFormFile file, string checksum, string depositFileName, string contentType) : IRequest<Result<WorkingFile?>>
{
    public Uri S3Root { get; } = s3Root;
    public string? Parent { get; } = parent;
    public string Slug { get; } = slug;
    public IFormFile File { get; } = file;
    public string Checksum { get; } = checksum;
    public string DepositFileName { get; } = depositFileName;
    public string ContentType { get; } = contentType;
}

public class UploadFileToDepositHandler(IAmazonS3 s3Client, IStorage storage) : IRequestHandler<UploadFileToDeposit, Result<WorkingFile?>>
{
    public async Task<Result<WorkingFile?>> Handle(UploadFileToDeposit request, CancellationToken cancellationToken)
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
        req.Metadata.Add(S3Helpers.OriginalNameMetadataKey, request.DepositFileName);
        try
        {
            response = await s3Client.PutObjectAsync(req, cancellationToken);
            var respChecksum = AwsChecksum.FromBase64ToHex(response.ChecksumSHA256);
            if(response is { ChecksumSHA256: not null } && respChecksum == request.Checksum)
            {
                var file = new WorkingFile
                {
                    LocalPath = fullKey.RemoveStart(s3Uri.Key)!,
                    ContentType = request.ContentType,
                    Digest = request.Checksum,
                    Size = response.ContentLength,
                    Name = request.DepositFileName
                };
                var saveResult = await storage.AddToMetsLike(s3Uri, IStorage.MetsLike, file, cancellationToken);
                if (saveResult.Success)
                {
                    return Result.Ok(file);
                }
            }

            return Result.Fail<WorkingFile>(ErrorCodes.BadRequest, $"Checksum on server did not match submitted checksum: server-calculated: {respChecksum}, submitted: {request.Checksum}");
        }
        catch (AmazonS3Exception s3E)
        {
            switch (s3E.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    return Result.Fail<WorkingFile?>(ErrorCodes.Conflict, "Conflicting resource at " + fullKey);
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<WorkingFile?>(ErrorCodes.Unauthorized, "Unauthorized for " + fullKey);
                case HttpStatusCode.BadRequest:
                    return Result.Fail<WorkingFile?>(ErrorCodes.BadRequest, "Bad Request");
                default:
                    return Result.Fail<WorkingFile>(ErrorCodes.UnknownError,
                        $"AWS returned status code {s3E.StatusCode} when trying to create {req.GetS3Uri()}.");
            }
        }
    }
}