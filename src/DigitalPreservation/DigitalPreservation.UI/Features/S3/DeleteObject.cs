using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

namespace DigitalPreservation.UI.Features.S3;

public class DeleteObject(Uri s3Root, string path, string metsETag) : IRequest<Result>
{
    public Uri S3Root { get; } = s3Root;
    public string Path { get; } = path;
    public string MetsETag { get; } = metsETag;
}

public class DeleteObjectHandler(
    IAmazonS3 s3Client,
    IStorage storage,
    IMetsManager metsManager) : IRequestHandler<DeleteObject, Result>
{
    public async Task<Result> Handle(DeleteObject request, CancellationToken cancellationToken)
    {
        // TODO same as other - put ALL this behind IStorage?
        var s3Uri = new AmazonS3Uri(request.S3Root);
        var dor = new DeleteObjectRequest
        {
            BucketName = s3Uri.Bucket,
            Key = s3Uri.Key + request.Path
        };
        try
        {
            var response = await s3Client.DeleteObjectAsync(dor, cancellationToken);
            if (response.HttpStatusCode == HttpStatusCode.NoContent)
            {
                var removeFromMetsResult = await storage.DeleteFromDepositFileSystem(s3Uri, request.Path, cancellationToken);
                if (removeFromMetsResult.Success)
                {
                    await metsManager.HandleDeleteObject(s3Uri.ToUri(), request.Path, request.MetsETag);
                }
                return removeFromMetsResult;
            }
            return ResultHelpers.FailFromAwsStatusCode<object>(response.HttpStatusCode, "Could not delete object.", dor.GetS3Uri());
        }
        catch (AmazonS3Exception s3E)
        {
            return ResultHelpers.FailFromS3Exception<object>(s3E, "Could not delete object", dor.GetS3Uri());
        }
    }
}