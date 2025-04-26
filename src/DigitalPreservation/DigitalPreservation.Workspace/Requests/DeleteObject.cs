using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using MediatR;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

namespace DigitalPreservation.Workspace.Requests;

public class DeleteObject(bool isBagItLayout, Uri rootUri, string path, bool isDirectory, string metsETag, bool fromFileSystem, bool fromMets) : IRequest<Result>
{
    public bool IsBagItLayout { get; } = isBagItLayout;
    public Uri RootUri { get; } = rootUri;
    public string Path { get; } = path;
    public bool IsDirectory { get; } = isDirectory;
    public string MetsETag { get; } = metsETag;
    public bool FromFileSystem { get; } = fromFileSystem;
    public bool FromMets { get; } = fromMets;
}

public class DeleteObjectHandler(
    IAmazonS3 s3Client,
    IStorage storage,
    IMetsManager metsManager) : IRequestHandler<DeleteObject, Result>
{
    public async Task<Result> Handle(DeleteObject request, CancellationToken cancellationToken)
    {
        // TODO same as other - put ALL this behind IStorage? YES
        var s3Uri = new AmazonS3Uri(request.RootUri);
        var keyPath = FolderNames.GetPathPrefix(request.IsBagItLayout) + request.Path;
        var dor = new DeleteObjectRequest
        {
            BucketName = s3Uri.Bucket,
            Key = s3Uri.Key + keyPath
        };
        if (request.IsDirectory && !dor.Key.EndsWith('/'))
        {
            dor.Key += "/";
        }
        try
        {
            if (request.FromFileSystem)
            {
                var response = await s3Client.DeleteObjectAsync(dor, cancellationToken);
                if (response.HttpStatusCode == HttpStatusCode.NoContent)
                {
                    var removeJson = await storage.DeleteFromDepositFileSystem(request.RootUri, keyPath, false, cancellationToken);
                    if(removeJson.Failure)
                    {
                        return Result.Fail(removeJson.ErrorCode ?? ErrorCodes.UnknownError, 
                            "Could not delete object from DepositFileSystem JSON: " + removeJson.ErrorMessage);
                    }
                }
                else
                {
                    return ResultHelpers.FailFromAwsStatusCode<object>(response.HttpStatusCode, "Could not delete object from S3.", dor.GetS3Uri());
                }

                if (request.FromMets)
                {
                    var deleteFromMetsResult = await metsManager.HandleDeleteObject(request.RootUri, request.Path, request.MetsETag);
                    return deleteFromMetsResult;
                }
            }
            return Result.Ok();
        }
        catch (AmazonS3Exception s3E)
        {
            return ResultHelpers.FailFromS3Exception<object>(s3E, "Could not delete object", dor.GetS3Uri());
        }
    }
}