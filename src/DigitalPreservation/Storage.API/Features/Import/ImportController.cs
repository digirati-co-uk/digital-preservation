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
    /// Build a "diff" importJob payload for an existing or non-existing archival group by comparing it to files hosted at 'source'.
    /// </summary>
    /// <param name="archivalGroupPathUnderRoot">Path to item in Fedora (e.g. path/to/item)</param>
    /// <param name="source">S3 URI containing items to create diff from (e.g. s3://uol-expts-staging-01/ocfl-example)</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Import job JSON payload</returns>
    [HttpGet("diff/{*archivalGroupPathUnderRoot}", Name = "DiffImportJob")]
    [Produces<ImportJob>]
    [Produces("application/json")]
    public async Task<IActionResult> GetImportJob(
        [FromRoute] string archivalGroupPathUnderRoot,
        [FromQuery] string source,
        CancellationToken cancellationToken = default)
    {
        archivalGroupPathUnderRoot = Uri.UnescapeDataString(archivalGroupPathUnderRoot);
        
        var archivalGroupResult = await mediator.Send(new GetValidatedArchivalGroupForImportJob(archivalGroupPathUnderRoot), cancellationToken);
        
        // This is either an existing Archival Group (result.Value not null),
        // or a 404 where the immediate parent is a Container that is not itself part of an Archival Group.
        if (archivalGroupResult.Failure)
        {
            return this.StatusResponseFromResult(archivalGroupResult);
        }
        
        // So now evaluate the source:
        var sourceUri = new Uri(Uri.UnescapeDataString(source));
        var importJobResult = await mediator.Send(
            new GetDiffImportJob(
                archivalGroupResult.Value, 
                sourceUri, 
                archivalGroupPathUnderRoot, 
                archivalGroupName: null,
                errorIfMissingChecksum: true, 
                relyOnMetsLike: true), cancellationToken);
        
        return this.StatusResponseFromResult(importJobResult);
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