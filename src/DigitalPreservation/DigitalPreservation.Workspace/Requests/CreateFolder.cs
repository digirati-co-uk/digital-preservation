using System.Net;
using System.Text.Encodings.Web;
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

public class CreateFolder(
    bool isBagItLayout,
    Uri rootUri, 
    string name, 
    string newFolderSlug, 
    string? parent, 
    string metsETag) : IRequest<Result<WorkingDirectory?>>
{
    public bool IsBagItLayout { get; } = isBagItLayout;
    public Uri RootUri { get; } = rootUri;
    public string Name { get; } = name;
    public string NewFolderSlug { get; } = newFolderSlug;
    public string? Parent { get; } = parent;
    public string MetsETag { get; } = metsETag;
}


public class CreateFolderHandler(
    IAmazonS3 s3Client,
    IStorage storage,
    IMetsManager metsManager) : IRequestHandler<CreateFolder, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(CreateFolder request, CancellationToken cancellationToken)
    {
        // Should this have IStorage rather than IAmazonS3? Probably not, because it's independent of the preservation api
        // It's a client putting things in S3 by itself. So?
        
        // TODO: Should all of this be behind IStorage? YES, this can work off a filesystem impl of IStorage

        var s3Uri = new AmazonS3Uri(request.RootUri);
        var localPath = FolderNames.GetPathPrefix(request.IsBagItLayout) + request.Parent;
        var fullKey = StringUtils.BuildPath(false, s3Uri.Key, localPath, request.NewFolderSlug);
        if (!fullKey.EndsWith("/"))
        {
            fullKey += "/";
        }

        var pReq = new PutObjectRequest
        {
            BucketName = s3Uri.Bucket,
            Key = fullKey
        };
        pReq.Metadata.Add(S3Helpers.OriginalNameMetadataKey, WebUtility.UrlEncode(request.Name));
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
                LocalPath = fullKey.RemoveStart(s3Uri.Key)!.TrimEnd('/'),
                Name = request.Name,
                Modified = headResponse.LastModified.ToUniversalTime()
            };
            var newRootResult = await storage.AddToDepositFileSystem(request.RootUri, dir, cancellationToken);
            var dirForMets = request.IsBagItLayout ? dir.ToRootLayout() : dir;
            if (newRootResult.Success)
            {
                await metsManager.HandleCreateFolder(request.RootUri, dirForMets, request.MetsETag);
                return Result.Ok(dir);
            }
            return Result.Generify<WorkingDirectory?>(newRootResult);
        }
        catch (AmazonS3Exception s3E)
        {
            var exResult = ResultHelpers.FailFromS3Exception<WorkingDirectory>(s3E, "Could not create folder", pReq.GetS3Uri());
            return exResult;
        }
    }
}