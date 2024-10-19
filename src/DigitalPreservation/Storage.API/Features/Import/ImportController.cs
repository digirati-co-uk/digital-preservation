using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Import.Requests;

namespace Storage.API.Features.Import;

[Route("[controller]")]
[ApiController]
public class ImportController(IMediator mediator) : Controller
{
    /// <summary>
    /// Build an 'importJob' payload for an existing archival group by comparing it to files hosted at 'source'.
    /// </summary>
    /// <param name="archivalGroupPathUnderRoot">Path to item in Fedora (e.g. path/to/item)</param>
    /// <param name="source">S3 URI containing items to create diff from (e.g. s3://uol-expts-staging-01/ocfl-example)</param>
    /// <returns>Import job JSON payload</returns>
    [HttpGet("{*archivalGroupPathUnderRoot}", Name = "ImportJob")]
    [Produces<ImportJob>]
    [Produces("application/json")]
    public async Task<IActionResult> GetImportJob([FromRoute] string archivalGroupPathUnderRoot, [FromQuery] string source)
    {
        archivalGroupPathUnderRoot = Uri.UnescapeDataString(archivalGroupPathUnderRoot);
        
        var archivalGroupResult = await mediator.Send(new GetValidatedArchivalGroupForImportJob(archivalGroupPathUnderRoot));
        
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
                relyOnMetsLike: true));
        
        return this.StatusResponseFromResult(importJobResult);
    }
    
}