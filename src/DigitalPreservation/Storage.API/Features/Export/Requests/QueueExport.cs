using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora.Model;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class QueueExport(ExportResource export) : IRequest<Result<ExportResource>>
{
    public ExportResource Export { get; } = export;
}

public class QueueExportHandler(
    ILogger<QueueExportHandler> logger,
    IIdentityService identityService,
    IExportResultStore exportResultStore,
    Converters converters,
    IExportQueue exportQueue) : IRequestHandler<QueueExport, Result<ExportResource>>
{
    public async Task<Result<ExportResource>> Handle(QueueExport request, CancellationToken cancellationToken)
    {
        var runningExports = await exportResultStore
            .GetUnfinishedExportsForArchivalGroup(request.Export.ArchivalGroup, cancellationToken);
        if (runningExports.Success && runningExports.Value!.Count > 0)
        {
            return Result.FailNotNull<ExportResource>(ErrorCodes.Conflict, 
                $"There is an unfinished export ({runningExports.Value[0]}) for Archival Group {request.Export.ArchivalGroup.GetPathUnderRoot()}");
        }
        if (runningExports.Failure)
        {
            return Result.FailNotNull<ExportResource>(ErrorCodes.UnknownError, 
                $"Could not check for running exports for Archival Group {request.Export.ArchivalGroup}");
        }
        
        // TODO: Validate Export Request
        // request.Export.ArchivalGroup is a real ArchivalGroup
        // request.Export.Destination is an accessible location
        //    - var destination = new AmazonS3Uri(export.Destination);
        // Any access control concerns, and whitelisting of S3 locations/buckets that can be exported to
        
        var identifier = identityService.MintIdentity(nameof(ExportResource));
        request.Export.Id = converters.GetExportResultId(identifier);
        var createResult = await exportResultStore.CreateExportResult(identifier, request.Export, cancellationToken);
        if (createResult.Success)
        {
            logger.LogInformation($"About to queue export request {identifier}");
            await exportQueue.QueueRequest(identifier, cancellationToken);
            // now retrieve again
            var storedExportResult = await exportResultStore.GetExportResult(identifier, cancellationToken);
            if (storedExportResult is { Success: true, Value: not null })
            {
                return Result.OkNotNull(storedExportResult.Value);
            }
            return Result.FailNotNull<ExportResource>(ErrorCodes.UnknownError, "Unable to retrieve export result");
        }
        return Result.FailNotNull<ExportResource>(ErrorCodes.UnknownError, "Unable to create and queue export.");
    }
}