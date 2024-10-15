using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Features.S3;

public class GetWorkingDirectory(Uri s3Uri, bool readFromS3, bool writeToStorage) : IRequest<Result<WorkingDirectory?>>
{
    public Uri S3Uri { get; } = s3Uri;
    public bool ReadFromS3 { get; } = readFromS3;
    public bool WriteToStorage { get; } = writeToStorage;
}

public class ReadS3Handler(IStorage storage) : IRequestHandler<GetWorkingDirectory, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(GetWorkingDirectory request, CancellationToken cancellationToken)
    {
        if (request.ReadFromS3)
        {
            var fromScratch = await storage.GenerateMetsLike(
                new AmazonS3Uri(request.S3Uri), request.WriteToStorage, cancellationToken);
            return fromScratch;
        }
        var fromMets = await storage.ReadMetsLike(
            new AmazonS3Uri(request.S3Uri), IStorage.MetsLike, cancellationToken);
        if (fromMets.Success)
        {
            return fromMets;
        }
        if (fromMets.ErrorCode == ErrorCodes.NotFound && request.WriteToStorage)
        {
            var fromScratch = await storage.GenerateMetsLike(
                new AmazonS3Uri(request.S3Uri), true, cancellationToken);
            return fromScratch;
        }
        return fromMets;
    }
}