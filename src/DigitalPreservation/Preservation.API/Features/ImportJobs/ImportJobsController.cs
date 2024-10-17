using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Core.Web;
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
    
    private IActionResult? ValidateDeposit(Deposit existingDeposit)
    {
        if (existingDeposit.Status == DepositStates.Exporting) return BadRequest("Deposit is being exported");
        if (existingDeposit.ArchivalGroup == null) return BadRequest("Deposit requires Archival Group");
        return null;
    }
}