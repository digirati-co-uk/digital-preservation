using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs.Requests;

namespace Preservation.API.Features.Deposits;


[Route("[controller]")]
[ApiController]
public class DepositsController(IMediator mediator) : Controller
{
    [HttpGet(Name = "ListDeposits")]
    [ProducesResponseType<List<Deposit>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ListDeposits([FromQuery] DepositQuery? query) // 
    {
        var result = await mediator.Send(new GetDeposits(query));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpGet("{id}", Name = "GetDeposit")]
    [ProducesResponseType<List<Deposit>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDeposit([FromRoute] string id)
    {
        var result = await mediator.Send(new GetDeposit(id));
        return this.StatusResponseFromResult(result);
    }
    
        
    [HttpPatch("{id}", Name = "PatchDeposit")]
    [ProducesResponseType<List<Deposit>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> PatchDeposit([FromRoute] string id, [FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new PatchDeposit(deposit));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpDelete("{id}", Name = "DeleteDeposit")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> DeleteDeposit([FromRoute] string id)
    {
        var result = await mediator.Send(new DeleteDeposit(id));
        return this.StatusResponseFromResult(result, 204);
    }
    
    
    [HttpPost(Name = "CreateDeposit")]
    [ProducesResponseType<List<Deposit>>(201, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> CreateDeposit([FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new CreateDeposit(deposit));
        Uri? createdLocation = null;
        if (result.Success)
        {
            createdLocation = new Uri($"/deposits/{result.Value!.Id!.GetSlug()}", UriKind.Relative);
        }
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    


    
}