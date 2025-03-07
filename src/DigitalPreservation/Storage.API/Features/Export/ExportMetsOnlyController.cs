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
    ILogger<ExportMetsOnlyController> logger) : Controller
{
    [HttpPost(Name = "Export mets Synchronously")]
    [Produces<ExportResource>]
    [Produces("application/json")]
    public async Task<IActionResult> ExportQueue(
        [FromBody] ExportResource export,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Synchronously exporting METS export for {path}", export.ArchivalGroup.GetPathUnderRoot());
        var metsExportResult = await mediator.Send(new ExecuteExport(null, export, true), cancellationToken);
        return this.StatusResponseFromResult(metsExportResult);
    }
}
