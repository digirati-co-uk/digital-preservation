using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora.Model;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export.Requests;

public class QueueExport(ExportResource export, string callerIdentity) : IRequest<Result<ExportResource>>
{
    public ExportResource Export { get; } = export;
    public string CallerIdentity { get; } = callerIdentity;
}

public class QueueExportHandler(
    ILogger<QueueExportHandler> logger,
    IIdentityMinter identityMinter,
    IExportResultStore exportResultStore,
    Converters converters,
    IExportQueue exportQueue) : IRequestHandler<QueueExport, Result<ExportResource>>
{
    public async Task<Result<ExportResource>> Handle(QueueExport request, CancellationToken cancellationToken)
    {
        if (request.Export.ArchivalGroup == null)
        {
            return Result.FailNotNull<ExportResource>(ErrorCodes.BadRequest, "Export has no Archival Group");
        }
        if (!SafeRepositoryPath.IsRepositoryPath(request.Export.ArchivalGroup.GetPathUnderRoot(), out var pathReason))
        {
            return Result.FailNotNull<ExportResource>(ErrorCodes.BadRequest,
                $"Export Archival Group {request.Export.ArchivalGroup} is not a path under the repository root: {pathReason}");
        }
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
        // Any access control concerns, and whitelisting of S3 locations/buckets that can be exported to

        // Stamped server-side, the same as every other resource in the platform - unlike the rest of
        // the record, nothing here trusted the caller's own values before. A supplied createdBy is
        // still trusted, the same way an import job's lastModifiedBy is trusted (issue #273).
        var now = DateTime.UtcNow;
        request.Export.Created = now;
        request.Export.LastModified = now;
        request.Export.CreatedBy ??= converters.GetAgentUri(request.CallerIdentity);
        request.Export.LastModifiedBy = request.Export.CreatedBy;

        var identifier = identityMinter.MintIdentity(nameof(ExportResource));
        request.Export.Id = converters.GetExportResultId(identifier);
        var createResult = await exportResultStore.CreateExportResult(identifier, request.Export, cancellationToken);
        if (createResult.Success)
        {
            logger.LogInformation("About to queue export request {Identifier}", identifier);
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