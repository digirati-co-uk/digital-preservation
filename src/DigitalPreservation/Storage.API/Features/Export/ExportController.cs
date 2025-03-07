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
    [HttpPost(Name = "QueueExport")]
    [Produces<ExportResource>]
    [Produces("application/json")]
    public async Task<IActionResult> ExportQueue(
        [FromBody] ExportResource export, 
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Queuing export for {path}", export.ArchivalGroup.GetPathUnderRoot());
        var queueExportResult = await mediator.Send(new QueueExport(export), cancellationToken);
        logger.LogInformation("Returned from QueueExport");
        var createdLocation = queueExportResult.Success ? queueExportResult.Value!.Id : null;
        return this.StatusResponseFromResult(queueExportResult, 201, createdLocation);
    }

    [HttpGet("{identifier}", Name = "GetExport")]
    [Produces<ExportResource>]
    [Produces("application/json")]
    public async Task<IActionResult> GetExportResult(
        [FromRoute] string identifier,
        CancellationToken cancellationToken = default)
    {
        var currentStatusResult = await mediator.Send(new GetExportResult(identifier), cancellationToken);
        return this.StatusResponseFromResult(currentStatusResult);
    }
}