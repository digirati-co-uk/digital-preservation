using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs.Requests;

namespace Preservation.API.Features.ImportJobs;


[Route("deposits/{id}/[controller]")]
[ApiController]
public class ImportJobsController(IMediator mediator) : Controller
{    
    [HttpGet("diff", Name = "GetDiffImportJob")]
    [ProducesResponseType<ImportJob>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDiffImportJob([FromRoute] string id)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult.Failure)
        {
            return this.StatusResponseFromResult(depositResult);
        }
        var validationResult = ValidateDeposit(depositResult.Value!);
        if (validationResult != null) return validationResult;
        
        var result = await mediator.Send(new GetDiffImportJob(depositResult.Value!));
        return this.StatusResponseFromResult(result);
    }

    public async Task<IActionResult> ExecuteImportJob([FromRoute] string id, [FromBody] ImportJob importJob,
        CancellationToken cancellationToken)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult.Failure)
        {
            return this.StatusResponseFromResult(depositResult);
        }

        var deposit = depositResult.Value!;
        var validationResult = ValidateDeposit(deposit);
        if (validationResult != null) return validationResult;
        
        if (IsPostedDiffReference(importJob, Request.Path))
        {
            var diffImportJobResult = await mediator.Send(new GetDiffImportJob(deposit), cancellationToken);
            importJob = diffImportJobResult.Value!;
        }
        
        var executeImportJobResult = await mediator.Send(new ExecuteImportJob(importJob), cancellationToken);
        if (executeImportJobResult.Success)
        {
            var importJobResult = executeImportJobResult.Value!;
            return CreatedAtAction(nameof(GetImportJobResult), new { id, importJobId = importJobResult.Id!.GetSlug() }, importJobResult);
        }
        return this.StatusResponseFromResult(executeImportJobResult);
    }

    /// <summary>
    /// Get the status of an existing ImportJobResult - the result of executing an ImportJob
    /// </summary>
    /// <param name="id">Deposit id import job is for</param>
    /// <param name="importJobId">Unique import job identifier</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Status of ImportJobResult</returns>
    [HttpGet("results/{importJobId}")]
    public async Task<IActionResult> GetImportJobResult([FromRoute] string id, [FromRoute] string importJobId,
        CancellationToken cancellationToken)
    {
        var importJobResultResult = await mediator.Send(new GetImportJobResult(id), cancellationToken);
        return this.StatusResponseFromResult(importJobResultResult);
    }
    
    
    private IActionResult? ValidateDeposit(Deposit existingDeposit)
    {
        if (existingDeposit.Status == DepositStates.Exporting) return BadRequest("Deposit is being exported");
        if (existingDeposit.ArchivalGroup == null) return BadRequest("Deposit requires Archival Group");
        return null;
    }
    
    private bool IsPostedDiffReference(ImportJob importJob, PathString path)
    {
        // This is when the API caller posts a reference to the diff import job rather than an _actual_ job
        // means we have to build the diff now.
        if(importJob.Id!.ToString().EndsWith(path + "/diff"))
        {
            // We may want to be more flexible that this, e.g., allowing the DigitalObject to be set as part of the immediate diff execution
            if(    importJob.Deposit == null 
               && importJob.ArchivalGroup == null
               && importJob.ArchivalGroupName == null
               && importJob.ContainersToAdd.Count == 0
               && importJob.ContainersToDelete.Count == 0
               && importJob.BinariesToAdd.Count == 0
               && importJob.BinariesToDelete.Count == 0
               && importJob.BinariesToPatch.Count == 0)
            {
                return true;
            }
        }
        return false;
    }
}