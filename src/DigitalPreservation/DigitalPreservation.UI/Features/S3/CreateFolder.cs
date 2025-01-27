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

namespace DigitalPreservation.UI.Features.S3;

public class CreateFolder(Uri s3Root, string name, string newFolderSlug, string? parent) : IRequest<Result<WorkingDirectory?>>
{
    public Uri S3Root { get; } = s3Root;
    public string Name { get; } = name;
    public string NewFolderSlug { get; } = newFolderSlug;
    public string? Parent { get; } = parent;
}


public class CreateFolderHandler(
    IAmazonS3 s3Client,
    IStorage storage,
    IMetsManager metsManager) : IRequestHandler<CreateFolder, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(CreateFolder request, CancellationToken cancellationToken)
    {
        // Should this have IStorage rather than IAmazonS3? Probably not, because it's independent of the preservation api
        // It's a client putting things in S3 by itself.
        
        // TODO: Should all of this be behind IStorage?

        var s3Uri = new AmazonS3Uri(request.S3Root);
        var fullKey = StringUtils.BuildPath(false, s3Uri.Key, request.Parent, request.NewFolderSlug);
        if (!fullKey.EndsWith("/"))
        {
            fullKey += "/";
        }

        var pReq = new PutObjectRequest
        {
            BucketName = s3Uri.Bucket,
            Key = fullKey
        };
        pReq.Metadata.Add(S3Helpers.OriginalNameMetadataKey, request.Name);
        try
        {
            var response = await s3Client.PutObjectAsync(pReq, cancellationToken);
            if (response.HttpStatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
                return Result.Fail<WorkingDirectory>(ErrorCodes.UnknownError,
                    $"Could not create Directory at {s3Uri}. AWS response was '{response.HttpStatusCode}'.");
            
            // See note on file upload - need to find what lastmodified was set
            var headReq = new GetObjectMetadataRequest
            {
                BucketName = s3Uri.Bucket,
                Key = fullKey
            };
            var headResponse = await s3Client.GetObjectMetadataAsync(headReq, cancellationToken);
            var dir = new WorkingDirectory
            {
                LocalPath = fullKey.RemoveStart(s3Uri.Key)!,
                Name = request.Name,
                Modified = headResponse.LastModified.ToUniversalTime()
            };
            var newRootResult = await storage.AddToMetsLike(s3Uri, IStorage.MetsLike, dir, cancellationToken);
            if (newRootResult.Success)
            {
                await metsManager.HandleCreateFolder(s3Uri.ToUri(), dir);
                return Result.Ok(dir);
            }
            return Result.Generify<WorkingDirectory?>(newRootResult);
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingDirectory>(s3E, "Could not create folder", pReq.GetS3Uri());
            return exResult;
            // return Result.Generify<WorkingDirectory?>(exResult);
        }
    }
}