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
using Storage.Repository.Common.S3;

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
        // TODO: This needs to prevent overlapping calls (repeated requests for the same object, or two uploads trying to update METS)
        var s3Uri = new AmazonS3Uri(request.S3Root);
        var fullKey = StringUtils.BuildPath(false, s3Uri.Key, request.Parent, request.Slug);
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
            var response = await s3Client.PutObjectAsync(req, cancellationToken);
            var respChecksum = AwsChecksum.FromBase64ToHex(response.ChecksumSHA256);
            if(response is { ChecksumSHA256: not null } && respChecksum == request.Checksum)
            {
                // we need the Modified date that S3 set when it saved this file, which I don't think we can get 
                // without a further HEAD request:
                var headReq = new GetObjectMetadataRequest
                {
                    BucketName = s3Uri.Bucket,
                    Key = fullKey,
                    ChecksumMode = ChecksumMode.ENABLED
                };
                var headResponse = await s3Client.GetObjectMetadataAsync(headReq, cancellationToken);
                if (headResponse.ChecksumSHA256 != response.ChecksumSHA256)
                {
                    throw new Exception("HEAD checksum does not match PUT checksum");
                }
                var file = new WorkingFile
                {
                    LocalPath = fullKey.RemoveStart(s3Uri.Key)!,
                    ContentType = request.ContentType,
                    Digest = request.Checksum,
                    Size = request.File.Length,
                    Name = request.DepositFileName,
                    Modified = headResponse.LastModified.ToUniversalTime() // keep an eye on https://github.com/aws/aws-sdk-net/issues/1885
                };
                var saveResult = await storage.AddToMetsLike(s3Uri, IStorage.MetsLike, file, cancellationToken);
                if (saveResult.Success)
                {
                    return Result.Ok(file);
                }
                return Result.Generify<WorkingFile?>(saveResult);
            }
            return Result.Fail<WorkingFile>(ErrorCodes.BadRequest, $"Checksum on server did not match submitted checksum: server-calculated: {respChecksum}, submitted: {request.Checksum}");
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingFile>(s3E, "Unable to upload file", req.GetS3Uri());
            return exResult;
            // return Result.Generify<WorkingFile?>(exResult);
        }
    }
}