using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace.Requests;

public class GetWorkingDirectory(Uri s3Uri, bool readFromS3, bool writeToStorage, DateTime? lastModified = null) : IRequest<Result<WorkingDirectory?>>
{
    public Uri S3Uri { get; } = s3Uri;
    public bool ReadFromS3 { get; } = readFromS3;
    public bool WriteToStorage { get; } = writeToStorage;
    public DateTime? LastModified { get; } = lastModified;
}

public class ReadS3Handler(IStorage storage) : IRequestHandler<GetWorkingDirectory, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(GetWorkingDirectory request, CancellationToken cancellationToken)
    {
        var s3Uri = new AmazonS3Uri(request.S3Uri);
        if (request.ReadFromS3)
        {
            var fromScratch = await storage.GenerateDepositFileSystem(
               s3Uri, request.WriteToStorage, cancellationToken);
            return fromScratch;
        }
        var fromJson = await storage.ReadDepositFileSystem(s3Uri, cancellationToken);
        if (fromJson is { Success: true, Value: not null })
        {
            if (request.LastModified.HasValue && fromJson.Value.Modified < request.LastModified)
            {
                var fromScratch = await storage.GenerateDepositFileSystem(
                    s3Uri, true, cancellationToken);
                return fromScratch;
            }
            return fromJson;
        }
        if (fromJson.ErrorCode == ErrorCodes.NotFound && request.WriteToStorage)
        {
            var fromScratch = await storage.GenerateDepositFileSystem(
                s3Uri, true, cancellationToken);
            return fromScratch;
        }
        return fromJson;
    }
}