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
    ILogger<ImportController> logger) : ControllerBase
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
        // Routing has already decoded the route value once. A second decode turned "%252e%252e" into
        // "..", and "%2F%2Fevil.com" into "//evil.com", which relative-reference resolution then follows.
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
        logger.LogInformation("Executing import job {Path}", importJob.ArchivalGroup);
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

    /// <summary>
    /// Operator path for issue #366: clears an import job that is stuck Active with no heartbeat at
    /// all (dead-lettered, still queued, or mid-run on a pre-#354 importer) - the gap the automatic
    /// heartbeat-based reap deliberately leaves alone. Replaces the operator SQL that was the only
    /// remedy before this existed (issue #245). Protected by the same machine-auth requirement as
    /// every other route on this controller.
    /// </summary>
    [HttpPost("{jobIdentifier}/cancel", Name = "CancelStuckImportJob")]
    [Produces<ImportJobResult>]
    [Produces("application/json")]
    public async Task<IActionResult> CancelStuckImportJob(
        [FromRoute] string jobIdentifier,
        [FromBody] CancelImportJobBody body,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Operator cancel requested for stuck import job {JobId}: {Reason}", jobIdentifier, body.Reason);
        var cancelResult = await mediator.Send(new CancelStuckImportJob(jobIdentifier, body.Reason), cancellationToken);
        return this.StatusResponseFromResult(cancelResult);
    }

}

/// <summary>
/// Body for <see cref="ImportController.CancelStuckImportJob"/>. A reason is required (not just a
/// job id) so the record this leaves behind explains itself - the whole point of preferring this
/// endpoint over a silent row update (issue #245).
/// </summary>
public class CancelImportJobBody
{
    public required string Reason { get; set; }
}