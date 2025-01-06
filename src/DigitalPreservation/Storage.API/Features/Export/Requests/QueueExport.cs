using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora.Model;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class QueueExport(ExportResource export) : IRequest<Result<Uri>>
{
    public ExportResource Export { get; } = export;
}

public class QueueExportHandler(
    ILogger<QueueExportHandler> logger,
    IIdentityService identityService,
    IExportResultStore exportResultStore,
    Converters converters,
    IExportQueue exportQueue) : IRequestHandler<QueueExport, Result<Uri>>
{
    public async Task<Result<Uri>> Handle(QueueExport request, CancellationToken cancellationToken)
    {
        var runningExports = await exportResultStore
            .GetUnfinishedExportsForArchivalGroup(request.Export.ArchivalGroup, cancellationToken);
        if (runningExports.Success && runningExports.Value!.Count > 0)
        {
            return Result.FailNotNull<Uri>(ErrorCodes.Conflict, 
                $"There is an unfinished export ({runningExports.Value[0]}) for Archival Group {request.Export.ArchivalGroup}");
        }
        if (runningExports.Failure)
        {
            return Result.FailNotNull<Uri>(ErrorCodes.UnknownError, 
                $"Could not check for running exports for Archival Group {request.Export.ArchivalGroup}");
        }
        var identifier = identityService.MintIdentity(nameof(ExportResource));
        request.Export.Id = converters.GetExportResultId(identifier);
        var createResult = await exportResultStore.CreateExportResult(identifier, request.Export, cancellationToken);
        if (createResult.Success)
        {
            logger.LogInformation($"About to queue export request {identifier}");
            await exportQueue.QueueRequest(identifier, cancellationToken);
            return Result.OkNotNull(request.Export.Id);
        }
        return Result.FailNotNull<Uri>(ErrorCodes.UnknownError, "Unable to create and queue export.");
    }
}