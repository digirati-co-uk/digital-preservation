using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Features.Import;

[Route("[controller]")]
[ApiController]
public class ImportController(
    IMediator mediator,
    ILogger<ImportController> logger) : Controller
{

    /// <summary>
    /// Validate that an Archival Group can be created or edited here.
    /// </summary>
    /// <param name="archivalGroupPathUnderRoot"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("test-path/{*archivalGroupPathUnderRoot}", Name = "TestImportJobArchivalGroupPath")]
    [Produces<ArchivalGroup>]
    [Produces("application/json")]
    public async Task<IActionResult> TestArchivalGroupPath(
        [FromRoute] string archivalGroupPathUnderRoot,
        CancellationToken cancellationToken = default
    )
    { 
        archivalGroupPathUnderRoot = Uri.UnescapeDataString(archivalGroupPathUnderRoot);
        var archivalGroupResult = await mediator.Send(new GetValidatedArchivalGroupForImportJob(archivalGroupPathUnderRoot), cancellationToken);
        if (archivalGroupResult.Failure)
        {
            return this.StatusResponseFromResult(archivalGroupResult);
        }

        var dummyAgAsContainer = new ArchivalGroup();
        return Ok(dummyAgAsContainer);
    }
    


    [HttpPost(Name = "ExecuteImportJob")]
    [Produces<ImportJobResult>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecuteImportJob([FromBody] ImportJob importJob, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing import job {path}", importJob.ArchivalGroup);
        var queueImportJobResult = await mediator.Send(new QueueImportJob(importJob), cancellationToken);
        logger.LogInformation("Returned from QueueImportJob");
        return this.StatusResponseFromResult(queueImportJobResult, 201, queueImportJobResult.Value?.Id);
    }

    [HttpGet("results/{jobIdentifier}/{*archivalGroupPathUnderRoot}", Name = "ImportJobResult")]
    [Produces<ImportJobResult>]
    [Produces("application/json")]
    public async Task<IActionResult> GetImportJobResult(
        [FromRoute] string jobIdentifier,
        [FromRoute] string archivalGroupPathUnderRoot,
        CancellationToken cancellationToken = default)
    {
        var currentJobStatusResult = await mediator.Send(new GetImportJobResult(jobIdentifier, archivalGroupPathUnderRoot), cancellationToken);
        return this.StatusResponseFromResult(currentJobStatusResult);
    }

}