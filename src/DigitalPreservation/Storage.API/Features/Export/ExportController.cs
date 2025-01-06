using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Export.Requests;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;

namespace Storage.API.Features.Export;

[Route("[controller]")]
[ApiController]
public class ExportController(
    IMediator mediator,
    ILogger<ExportController> logger) : Controller
{
    [HttpPost(Name = "Export")]
    [Produces<ExportResource>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecuteExport(
        [FromBody] ExportResource export, 
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing export for {path}", export.ArchivalGroup.GetPathUnderRoot());
        var queueExportResult = await mediator.Send(new QueueExport(export), cancellationToken);
        logger.LogInformation("Returned from QueueExport");
        return this.StatusResponseFromResult(queueExportResult, 201, queueExportResult.Value);
    }

    [HttpGet("{exportIdentifier}", Name = "ExportResult")]
    [Produces<ExportResource>]
    [Produces("application/json")]
    public async Task<IActionResult> GetExportResult(
        [FromRoute] string exportIdentifier,
        CancellationToken cancellationToken = default)
    {
        var currentStatusResult = await mediator.Send(new GetExportResult(exportIdentifier), cancellationToken);
        return this.StatusResponseFromResult(currentStatusResult);
    }
}