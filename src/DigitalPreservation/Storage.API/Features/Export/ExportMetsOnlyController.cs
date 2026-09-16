using DigitalPreservation.Common.Model.Results;
using Storage.API.Fedora.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Export.Requests;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export;


[Route("[controller]")]
[ApiController]
public class ExportMetsOnlyController(
    IMediator mediator,
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
        logger.LogInformation("Synchronously exporting METS export for {Path}", export.ArchivalGroup.GetPathUnderRoot());
        var metsExportResult = await mediator.Send(new ExecuteExport(null, export, true), cancellationToken);
        return this.StatusResponseFromResult(metsExportResult);
    }
}
