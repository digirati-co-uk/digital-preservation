using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core;
using DigitalPreservation.Core.Auth;
using Microsoft.Extensions.Logging;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export;

/// <summary>
/// The validation and stamping QueueExportHandler and ExportMetsOnlyController both used to
/// hand-roll (issue #388 item 2.6): the ArchivalGroup presence/path check, the destination-bucket
/// check, and stamping Created/CreatedBy/LastModified/LastModifiedBy. Both copies were already
/// diverging in the past (#288 and #273 each fixed the controller copy for a step it was missing),
/// which is exactly the drift sharing one implementation prevents from happening again.
/// </summary>
public static class ExportValidation
{
    /// <summary>
    /// Fails if <paramref name="export"/> has no ArchivalGroup, that ArchivalGroup is not a path
    /// under the repository root, or Destination is not one of the platform's permitted deposit
    /// buckets. Callers that also need to check for an unfinished export against the same
    /// ArchivalGroup (QueueExportHandler) do that themselves - it needs a store this method has no
    /// reason to depend on.
    /// </summary>
    public static Result Validate(
        ExportResource export,
        AwsStorageOptions storageOptions,
        IClientDirectory clientDirectory,
        ILogger logger)
    {
        if (export.ArchivalGroup == null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Export has no Archival Group");
        }
        if (!SafeRepositoryPath.IsRepositoryPath(export.ArchivalGroup.GetPathUnderRoot(), out var pathReason))
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"Export Archival Group {export.ArchivalGroup} is not a path under the repository root: {pathReason}");
        }

        var permittedBuckets = PermittedExportBuckets.All(storageOptions, clientDirectory);
        if (!ExportDestination.IsInBucket(export.Destination, permittedBuckets, out var reason))
        {
            logger.LogWarning(
                "Refusing export destination {Destination}: {Reason}. Permitted buckets: {PermittedBuckets}",
                export.Destination, reason, permittedBuckets);
            return Result.Fail(ErrorCodes.BadRequest, reason);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Stamped server-side, the same as every other resource in the platform - unlike the rest of
    /// the record, nothing here trusted the caller's own values before. A supplied CreatedBy is
    /// still trusted, the same way an import job's LastModifiedBy is trusted (issue #273).
    /// </summary>
    public static void Stamp(ExportResource export, Converters converters, string callerIdentity)
    {
        var now = DateTime.UtcNow;
        export.Created = now;
        export.LastModified = now;
        export.CreatedBy ??= converters.GetAgentUri(callerIdentity);
        export.LastModifiedBy = export.CreatedBy;
    }
}
