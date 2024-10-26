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
    [HttpGet("{*archivalGroupPathUnderRoot}", Name = "ImportJob")]
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
            new GetImportJob(
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
        // TODO - not synchronously...
        var executeImportJobResult = await mediator.Send(new ExecuteImportJob(importJob), cancellationToken);
        return this.StatusResponseFromResult(executeImportJobResult, 201, executeImportJobResult.Value?.Id);
    }

    [HttpGet("{*archivalGroupPathUnderRoot}", Name = "ImportJob")]
    [Produces<ImportJobResult>]
    [Produces("application/json")]
    public async Task<IActionResult> GetImportJobResult(
        [FromRoute] string archivalGroupPathUnderRoot,
        [FromQuery] string transaction,
        CancellationToken cancellationToken = default)
    {
        // check up on the running job...
    }

}