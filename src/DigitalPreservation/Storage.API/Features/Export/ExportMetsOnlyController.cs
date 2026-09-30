using DigitalPreservation.Common.Model.Results;
using Storage.API.Fedora.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Storage.API.Features.Export.Requests;
using Storage.Repository.Common;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export;


[Route("[controller]")]
[ApiController]
public class ExportMetsOnlyController(
    IMediator mediator,
    IClientDirectory clientDirectory,
    IOptions<AwsStorageOptions> storageOptions,
    ILogger<ExportMetsOnlyController> logger) : ControllerBase
{
    [HttpPost(Name = "Export mets Synchronously")]
    [Produces<ExportResource>]
    [Produces("application/json")]
    public async Task<IActionResult> ExportQueue(
        [FromBody] ExportResource export,
        CancellationToken cancellationToken = default)
    {
        // Body-bound path: the route filter never sees it, so the same rule the queue handler applies.
        if (export.ArchivalGroup == null)
        {
            return ControllerX.GetProblemObjectResult(Result.Fail(ErrorCodes.BadRequest, "Export has no Archival Group"));
        }
        if (!SafeRepositoryPath.IsRepositoryPath(export.ArchivalGroup.GetPathUnderRoot(), out var pathReason))
        {
            return ControllerX.GetProblemObjectResult(Result.Fail(ErrorCodes.BadRequest,
                $"Export Archival Group {export.ArchivalGroup} is not a path under the repository root: {pathReason}"));
        }
        // This route calls ExecuteExport directly rather than going through QueueExportHandler, so
        // it must apply the same destination check itself - not relying on ExecuteExport, which the
        // queued path only runs later, after 201 has already been returned (issue #288).
        var permittedBuckets = new[] { storageOptions.Value.DefaultWorkingBucket }
            .Concat(clientDirectory.DepositBuckets).ToList();
        if (!ExportDestination.IsInBucket(export.Destination, permittedBuckets, out var reason))
        {
            logger.LogWarning(
                "Refusing export destination {Destination}: {Reason}. Permitted buckets: {PermittedBuckets}",
                export.Destination, reason, permittedBuckets);
            return ControllerX.GetProblemObjectResult(Result.Fail(ErrorCodes.BadRequest, reason));
        }
        logger.LogInformation("Synchronously exporting METS export for {Path}", export.ArchivalGroup.GetPathUnderRoot());
        var metsExportResult = await mediator.Send(new ExecuteExport(null, export, true), cancellationToken);
        return this.StatusResponseFromResult(metsExportResult);
    }
}
