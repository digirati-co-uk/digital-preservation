using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace.Requests;

public class GetWorkingDirectory(Uri rootUri, bool readFromS3, bool writeToStorage, DateTime? lastModified = null) : IRequest<Result<WorkingDirectory?>>
{
    public Uri RootUri { get; } = rootUri;
    public bool ReadFromS3 { get; } = readFromS3;
    public bool WriteToStorage { get; } = writeToStorage;
    public DateTime? LastModified { get; } = lastModified;
}

public class ReadS3Handler(IStorage storage) : IRequestHandler<GetWorkingDirectory, Result<WorkingDirectory?>>
{
    public async Task<Result<WorkingDirectory?>> Handle(GetWorkingDirectory request, CancellationToken cancellationToken)
    {
        if (request.ReadFromS3)
        {
            var metadataReader = await MetadataReader.Create(storage, request.RootUri);
            var fromScratch = await storage.GenerateDepositFileSystem(
               request.RootUri, request.WriteToStorage, metadataReader.Decorate, cancellationToken);
            return fromScratch;
        }
        var fromJson = await storage.ReadDepositFileSystem(request.RootUri, cancellationToken);
        if (fromJson is { Success: true, Value: not null })
        {
            if (request.LastModified.HasValue && fromJson.Value.Modified < request.LastModified)
            {
                var metadataReader = await MetadataReader.Create(storage, request.RootUri);
                var fromScratch = await storage.GenerateDepositFileSystem(
                    request.RootUri, true, metadataReader.Decorate, cancellationToken);
                return fromScratch;
            }
            return fromJson;
        }
        if (fromJson.ErrorCode == ErrorCodes.NotFound && request.WriteToStorage)
        {
            var metadataReader = await MetadataReader.Create(storage, request.RootUri);
            var fromScratch = await storage.GenerateDepositFileSystem(
                request.RootUri, true, metadataReader.Decorate, cancellationToken);
            return fromScratch;
        }
        return fromJson;
    }
}