using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;

namespace DigitalPreservation.UI.Features.S3;

public class ReadS3(Uri s3Uri) : IRequest<Result<MovingDirectory?>>
{
    public Uri S3Uri { get; set; } = s3Uri;
}

public class ReadS3Handler(IAmazonS3 s3Client) : IRequestHandler<ReadS3, Result<MovingDirectory?>>
{
    public async Task<Result<MovingDirectory?>> Handle(ReadS3 request, CancellationToken cancellationToken)
    {
        try
        {

            var s3Uri = new AmazonS3Uri(request.S3Uri);
            var listReq = new ListObjectsV2Request
            {
                BucketName = s3Uri.Bucket,
                Prefix = s3Uri.Key
            };
            List<S3Object> s3Objects = [];
            var resp = await s3Client.ListObjectsV2Async(listReq, cancellationToken);
            s3Objects.AddRange(resp.S3Objects);
            while (resp.IsTruncated)
            {
                listReq.ContinuationToken = resp.NextContinuationToken;
                resp = await s3Client.ListObjectsV2Async(listReq, cancellationToken);
                s3Objects.AddRange(resp.S3Objects);
            }

            var top = new MovingDirectory { LocalPath = String.Empty };

            // Create the directories
            foreach (var s3Object in s3Objects.OrderBy(o => o.Key.Replace('/', '~')))
            {
                if (s3Object.Key == s3Uri.Key)
                {
                    continue; // we don't want the deposit root itself, we already have that in top
                }
                var path = s3Object.Key.RemoveStart(s3Uri.Key)!;
                if (path.EndsWith('/'))
                {
                    var dir = top.FindDirectory(path, true);
                    dir.Modified = s3Object.LastModified;
                }
                else
                {
                    // a file
                    var dir = top.FindDirectory(path.GetParent(), true);
                    dir.Files.Add(new MovingFile
                    {
                        LocalPath = path,
                        ContentType = "?",
                        Size = s3Object.Size,
                        Modified = s3Object.LastModified
                    }); // need to get these from METS
                }
            }

            return Result.Ok<MovingDirectory?>(top);

        }
        catch (Exception e)
        {
            return Result.Fail<MovingDirectory?>(ErrorCodes.UnknownError, e.Message);
        }
    }
}

internal class SortableS3Object
{
    public SortableS3Object(string prefix, S3Object s3Object)
    {
        Path = s3Object.Key.RemoveStart(prefix)!;
        IsDirectory = Path.EndsWith('/');
        PathElements = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        SortableKey = Path.Replace('/', '~');
        Key = s3Object.Key;
        if (PathElements.Length > 0)
        {
            Parent = string.Join('/', PathElements.Take(PathElements.Length - 1).ToArray());
        }
        else
        {
            Parent = string.Empty;
        }
        Modified = s3Object.LastModified;
        Size = s3Object.Size;
    }
    public bool IsDirectory { get; } 
    public string[] PathElements { get; } 
    public string Key { get; }
    public string SortableKey { get; set; }
    public string Parent { get; set; }
    public string Path { get; set; }
    public DateTime Modified { get; set; }
    public long Size { get; set; }
}