using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

namespace DigitalPreservation.Workspace.Requests;

public class UploadFileToDeposit(
    bool isBagItLayout,
    Uri rootUri, 
    string? parent, 
    string slug, 
    Stream stream, 
    long size, 
    string checksum, 
    string depositFileName, 
    string contentType,
    string metsETag) : IRequest<Result<WorkingFile?>>
{
    public bool IsBagItLayout { get; } = isBagItLayout;
    public Uri RootUri { get; } = rootUri;
    public string? Parent { get; } = parent;
    public string Slug { get; } = slug;
    public Stream Stream { get; } = stream;
    public long Size { get; } = size;
    public string Checksum { get; } = checksum;
    public string DepositFileName { get; } = depositFileName;
    public string ContentType { get; } = contentType;
    public string MetsETag { get; } = metsETag;
}

public class UploadFileToDepositHandler(
    IAmazonS3 s3Client,
    IStorage storage,
    IMetsManager metsManager) : IRequestHandler<UploadFileToDeposit, Result<WorkingFile?>>
{
    public async Task<Result<WorkingFile?>> Handle(UploadFileToDeposit request, CancellationToken cancellationToken)
    {
        // TODO: This needs to prevent overlapping calls (repeated requests for the same object, or two uploads trying to update METS)
        var s3Uri = new AmazonS3Uri(request.RootUri);
        var keyPath = FolderNames.GetPathPrefix(request.IsBagItLayout) + request.Parent;
        var fullKey = StringUtils.BuildPath(false, s3Uri.Key, keyPath, request.Slug);
        var req = new PutObjectRequest
        {
            BucketName = s3Uri.Bucket,
            Key = fullKey,
            ContentType = request.ContentType,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            InputStream = request.Stream
        };
        req.Metadata.Add(S3Helpers.OriginalNameMetadataKey, WebUtility.UrlEncode(request.DepositFileName));
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
                
                var s3AssignedContentType = headResponse.Headers.ContentType;
                var contentTypeToBeStored = request.ContentType;
                if (contentTypeToBeStored.IsNullOrWhiteSpace())
                {
                    contentTypeToBeStored = s3AssignedContentType.HasText() ? s3AssignedContentType : ContentTypes.NotIdentified;
                }
                var file = new WorkingFile
                {
                    LocalPath = fullKey.RemoveStart(s3Uri.Key)!,
                    ContentType = contentTypeToBeStored,
                    Digest = request.Checksum.ToLowerInvariant(),
                    Size = request.Size,
                    Name = request.DepositFileName,
                    Modified = headResponse.LastModified.ToUniversalTime() // keep an eye on https://github.com/aws/aws-sdk-net/issues/1885
                };
                var saveResult = await storage.AddToDepositFileSystem(request.RootUri, file, cancellationToken);
                if (saveResult.Success)
                {
                    var result = await metsManager.HandleSingleFileUpload(request.RootUri, file, request.MetsETag);
                    if (result.Success)
                    {
                        return Result.Ok(file);
                    }
                    return Result.Fail<WorkingFile>(result.ErrorCode!, result.ErrorMessage);
                }
                return Result.Generify<WorkingFile?>(saveResult);
            }
            // The file we just saved in the Deposit does not the same checksum we think it should have.
            // We need to delete the file from the Deposit (see Azure 105199)
            var checksumMessage =
                $"Checksum on server did not match submitted checksum: server-calculated: {respChecksum}, submitted: {request.Checksum}";
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = s3Uri.Bucket,
                Key = fullKey
            };
            var deleteResponse = await s3Client.DeleteObjectAsync(deleteRequest, cancellationToken);
            if (deleteResponse.HttpStatusCode == HttpStatusCode.NoContent)
            {
                return Result.Fail<WorkingFile>(ErrorCodes.BadRequest, checksumMessage);
            }
            return Result.Fail<WorkingFile>(ErrorCodes.BadRequest, 
                checksumMessage + " - and could not delete object from Deposit: " + fullKey.RemoveStart(s3Uri.Key)!);
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingFile>(s3E, "Unable to upload file", req.GetS3Uri());
            return exResult;
        }
    }
}