using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Features.S3;

public class ReadS3(Uri s3Uri, bool fetchMetadata) : IRequest<Result<WorkingDirectory?>>
{
    public Uri S3Uri { get; } = s3Uri;
    public bool FetchMetadata { get; } = fetchMetadata;
}

public class ReadS3Handler(IStorage storage) : IRequestHandler<ReadS3, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(ReadS3 request, CancellationToken cancellationToken)
    {
        if (request.FetchMetadata)
        {
            var fromScratch = await storage.GenerateMetsLike(new AmazonS3Uri(request.S3Uri), cancellationToken);
            return fromScratch;
        }
        var fromMets = await storage.ReadMetsLike(new AmazonS3Uri(request.S3Uri), IStorage.MetsLike, cancellationToken);
        return fromMets;
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