using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora;
using Storage.Repository.Common;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class ExecuteExport(string identifier, ExportResource export) : IRequest<Result<ExportResource>>
{
    public string Identifier { get; } = identifier;
    public ExportResource Export { get; } = export;
}

public class ExecuteExportHandler(
    IStorage storage,
    IStorageMapper storageMapper,
    IAmazonS3 s3Client,
    IExportResultStore exportResultStore,
    ILogger<ExecuteExportHandler> logger) : IRequestHandler<ExecuteExport, Result<ExportResource>>
{
    public async Task<Result<ExportResource>> Handle(ExecuteExport request, CancellationToken cancellationToken)
    {
        var export = request.Export;
        var destination = new AmazonS3Uri(export.Destination);
        var destinationBucket = destination.Bucket;
        var destinationKey = destination.Key;
        if (destinationKey.EndsWith('/')) destinationKey = destinationKey[..^1];
        
        export.Files = [];
        var errors = new List<Error>();
        try
        {
            var storageMap = await storageMapper.GetStorageMap(export.ArchivalGroup, export.SourceVersion);
            export.DateBegun = DateTime.UtcNow;
            foreach (var file in storageMap.Files)
            {
                var sourceKey = SafeJoin(storageMap.ObjectPath, file.Value.FullPath);
                var destKey = SafeJoin(destinationKey, file.Key);
                var req = new CopyObjectRequest
                {
                    SourceBucket = storageMap.Root,
                    SourceKey = sourceKey,
                    DestinationBucket = destinationBucket,
                    DestinationKey = destKey,
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                var resp = await s3Client.CopyObjectAsync(req, cancellationToken);
                var hexChecksum = AwsChecksum.FromBase64ToHex(resp.ChecksumSHA256);
                if(resp is { ChecksumSHA256: not null } && hexChecksum == file.Value.Hash)
                {
                    export.Files.Add(req.GetDestinationS3Uri());
                }
                else
                {
                    errors.Add(new Error
                    {
                        Id = req.GetDestinationS3Uri(),
                        Message = "Checksum of moved file " +
                          $"[ source: {req.GetSourceS3Uri()} - {file.Value.Hash}, dest: {req.GetDestinationS3Uri()} - {hexChecksum} ]" +
                          " does not match expected value"
                    });
                }
            }

            var newWd = await storage.GenerateDepositFileSystem(destination, true, cancellationToken);
            // TODO: validate that newWd.Value matches what we expected from storageMap.Files
            export.DateFinished = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            errors.Add(new Error
            {
                Id = new Uri(export.Id + "#error"),
                Message = ex.Message
            });
        }

        export.Errors = errors.ToArray();
        await exportResultStore.UpdateExportResult(request.Identifier, export, cancellationToken);
        return Result.OkNotNull(export);
    }
    
    private static string SafeJoin(params string[] parts) => string.Join("/", parts).Replace("//", "/");
}