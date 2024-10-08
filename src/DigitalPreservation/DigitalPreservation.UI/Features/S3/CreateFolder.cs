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

public class CreateFolder(Uri s3Root, string name, string newFolderSlug, string? parent) : IRequest<Result<WorkingDirectory?>>
{
    public Uri S3Root { get; } = s3Root;
    public string Name { get; } = name;
    public string NewFolderSlug { get; } = newFolderSlug;
    public string? Parent { get; } = parent;
}


public class CreateFolderHandler(IAmazonS3 s3Client) : IRequestHandler<CreateFolder, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(CreateFolder request, CancellationToken cancellationToken)
    {
        // Should this have IStorage rather than IAmazonS3? Probably not, because it's independent of the preservation api
        // It's a client putting things in S3 by itself.
        
        // TODO: Edit METS; use request.Name

        PutObjectResponse? response = null;
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
        try
        {
            response = await s3Client.PutObjectAsync(pReq, cancellationToken);
            if (response.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                var dir = new WorkingDirectory { LocalPath = fullKey.RemoveStart(s3Uri.Key)! };
                return Result.Ok(dir);
            }
        }
        catch (AmazonS3Exception s3E)
        {
            switch (s3E.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    return Result.Fail<WorkingDirectory?>(ErrorCodes.Conflict, "Conflicting resource at " + fullKey);
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<WorkingDirectory?>(ErrorCodes.Unauthorized, "Unauthorized for " + fullKey);
                case HttpStatusCode.BadRequest:
                    return Result.Fail<WorkingDirectory?>(ErrorCodes.BadRequest, "Bad Request");
                default:
                    return Result.Fail<WorkingDirectory>(ErrorCodes.UnknownError,
                        $"AWS returned status code {s3E.StatusCode} when trying to create {pReq.GetS3Uri()}.");
            }
        }
        return Result.Fail<WorkingDirectory>(ErrorCodes.UnknownError,
            $"Could not create Directory at {s3Uri}.");
    }
}