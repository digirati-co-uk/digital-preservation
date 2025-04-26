using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Storage.API.Fedora;
using Storage.Repository.Common;
using Storage.Repository.Common.Mets;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class ExecuteExport(string? identifier, ExportResource export, bool metsOnly = false) : IRequest<Result<ExportResource>>
{
    public string? Identifier { get; } = identifier;
    public ExportResource Export { get; } = export;
    
    public bool MetsOnly { get; } = metsOnly;
}

public class ExecuteExportHandler(
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
        logger.LogInformation("Executing Export {RequestIdentifier} for {ExportArchivalGroup}; metOnly: {RequestMetsOnly}", 
            request.Identifier, request.Export.ArchivalGroup, request.MetsOnly);
        try
        {
            var storageMap = await storageMapper.GetStorageMap(export.ArchivalGroup, export.SourceVersion);
            if (export.SourceVersion.HasText())
            {
                if (storageMap.Version.OcflVersion != export.SourceVersion)
                {
                    return Result.FailNotNull<ExportResource>(ErrorCodes.Conflict, 
                        $"Storage map version {storageMap.Version.OcflVersion} does not match requested version {export.SourceVersion}.");
                }
            }

            export.SourceVersion = storageMap.Version.OcflVersion;
            export.DateBegun = DateTime.UtcNow;
            foreach (var file in storageMap.Files)
            {
                if (request.MetsOnly && !MetsUtils.IsMetsFile(file.Value.FullPath))
                {
                    continue;
                }
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

            // Remove this, so that a new export DOES NOT have a __metslike.json - which is a Preservation API concern.
            // The Preservation API can attempt to create one if this file is absent.
            // var newWd = await storage.GenerateDepositFileSystem(destination, true, cancellationToken);
            // TODO: validate that newWd.Value matches what we expected from storageMap.Files
            export.DateFinished = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not Execute Export {RequestIdentifier} for {ExportArchivalGroup}; metOnly: {RequestMetsOnly}", 
                request.Identifier, request.Export.ArchivalGroup, request.MetsOnly);
            errors.Add(new Error
            {
                Id = new Uri(export.Id + "#error"),
                Message = ex.Message
            });
        }

        export.Errors = errors.ToArray();
        if (!request.MetsOnly && request.Identifier.HasText())
        {
            await exportResultStore.UpdateExportResult(request.Identifier, export, cancellationToken);
        }

        return Result.OkNotNull(export);
    }
    
    private static string SafeJoin(params string[] parts) => string.Join("/", parts).Replace("//", "/");
}