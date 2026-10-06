using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.Extensions.Options;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;
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
    IExportQueue exportQueue,
    IClientDirectory clientDirectory,
    IOptions<AwsStorageOptions> storageOptions) : IRequestHandler<QueueExport, Result<ExportResource>>
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

        // Not checked here (out of scope for #288): that the Archival Group really exists, and that
        // the destination is reachable. The destination must be one of the deposit buckets, refused
        // before anything is stamped, persisted or queued.
        var permittedBuckets = PermittedExportBuckets.All(storageOptions.Value, clientDirectory);
        if (!ExportDestination.IsInBucket(request.Export.Destination, permittedBuckets, out var reason))
        {
            logger.LogWarning(
                "Refusing export destination {Destination}: {Reason}. Permitted buckets: {PermittedBuckets}",
                request.Export.Destination, reason, permittedBuckets);
            return Result.FailNotNull<ExportResource>(ErrorCodes.BadRequest, reason);
        }

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
