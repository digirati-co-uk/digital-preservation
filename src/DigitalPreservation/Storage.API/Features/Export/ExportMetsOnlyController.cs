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
    Converters converters,
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
        // Body-bound path: the route filter never sees it, so the same rule the queue handler
        // applies - this route calls ExecuteExport directly rather than going through
        // QueueExportHandler, so it must apply the same checks itself, not rely on ExecuteExport,
        // which the queued path only runs later, after 201 has already been returned (issue #288).
        var validation = ExportValidation.Validate(export, storageOptions.Value, clientDirectory, logger);
        if (validation.Failure)
        {
            return ControllerX.GetProblemObjectResult(validation);
        }

        // Not stored (there is no QueueExportHandler step for this synchronous route), so the
        // response is the only place these fields can be set (issue #273).
        ExportValidation.Stamp(export, converters, User.GetCallerIdentity());

        logger.LogInformation("Synchronously exporting METS export for {Path}", export.ArchivalGroup.GetPathUnderRoot());
        var metsExportResult = await mediator.Send(new ExecuteExport(null, export, true), cancellationToken);
        return this.StatusResponseFromResult(metsExportResult);
    }
}
