using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs.Requests;

namespace Preservation.API.Features.ImportJobs;


[Route("deposits/{depositId}/[controller]")]
[ApiController]
public class ImportJobsController(IMediator mediator) : Controller
{    
    [HttpGet("diff", Name = "GetDiffImportJob")]
    [ProducesResponseType<ImportJob>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDiffImportJob([FromRoute] string depositId)
    {
        var depositResult = await mediator.Send(new GetDeposit(depositId));
        if (depositResult.Failure)
        {
            return this.StatusResponseFromResult(depositResult);
        }
        var validationResult = ValidateDeposit(depositResult.Value!);
        if (validationResult != null) return this.StatusResponseFromResult(validationResult);
        
        var result = await mediator.Send(new GetDiffImportJob(depositResult.Value!));
        return this.StatusResponseFromResult(result);
    }

    public async Task<IActionResult> ExecuteImportJob([FromRoute] string depositId, [FromBody] ImportJob importJob,
        CancellationToken cancellationToken)
    {
        var depositResult = await mediator.Send(new GetDeposit(depositId), cancellationToken);
        if (depositResult.Failure)
        {
            return this.StatusResponseFromResult(depositResult);
        }

        var deposit = depositResult.Value!;
        var validationResult = ValidateDeposit(deposit);
        if (validationResult != null) return this.StatusResponseFromResult(validationResult);
        
        if (IsPostedDiffReference(importJob, Request.Path))
        {
            var diffImportJobResult = await mediator.Send(new GetDiffImportJob(deposit), cancellationToken);
            importJob = diffImportJobResult.Value!;
        }

        importJob.Deposit = deposit.Id;
        var executeImportJobResult = await mediator.Send(new ExecuteImportJob(importJob), cancellationToken);
        return this.StatusResponseFromResult(executeImportJobResult, 201, executeImportJobResult.Value?.Id);
    }
    
    [HttpGet("results", Name = "GetImportJobResults")]
    [ProducesResponseType<List<ImportJobResult>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetImportJobResult([FromRoute] string depositId)
    {
        var result = await mediator.Send(new GetImportJobResultsForDeposit(depositId));
        return this.StatusResponseFromResult(result);
    }


    /// <summary>
    /// Get the status of an existing ImportJobResult - the result of executing an ImportJob
    /// </summary>
    /// <param name="depositId">Deposit depositId import job is for</param>
    /// <param name="importJobId">Unique import job identifier</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Status of ImportJobResult</returns>
    [HttpGet("results/{importJobId}")]
    public async Task<IActionResult> GetImportJobResult([FromRoute] string depositId, [FromRoute] string importJobId,
        CancellationToken cancellationToken)
    {
        var importJobResultResult = await mediator.Send(new GetImportJobResult(depositId, importJobId), cancellationToken);
        return this.StatusResponseFromResult(importJobResultResult);
    }
    
    
    private Result? ValidateDeposit(Deposit existingDeposit)
    {
        if (existingDeposit.Status == DepositStates.Exporting)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Deposit is being exported");
        }
        if (existingDeposit.ArchivalGroup == null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Deposit requires Archival Group");
        }
        return null;
    }
    
    private bool IsPostedDiffReference(ImportJob importJob, PathString path)
    {
        // This is when the API caller posts a reference to the diff import job rather than an _actual_ job
        // means we have to build the diff now.
        if(importJob.Id!.ToString().EndsWith(path + "/diff"))
        {
            // We may want to be more flexible that this, e.g., allowing the DigitalObject to be set as part of the immediate diff execution
            if(   importJob.ContainersToAdd.Count == 0
               && importJob.ContainersToDelete.Count == 0
               && importJob.BinariesToAdd.Count == 0
               && importJob.BinariesToDelete.Count == 0
               && importJob.BinariesToPatch.Count == 0
               && importJob.ContainersToRename.Count == 0
               && importJob.BinariesToRename.Count == 0)
            {
                return true;
            }
        }
        return false;
    }
}