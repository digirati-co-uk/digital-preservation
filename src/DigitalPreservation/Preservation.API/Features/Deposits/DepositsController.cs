using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;

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
    
    [HttpPost(Name = "CreateDeposit")]
    [ProducesResponseType<List<Deposit>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> CreateDeposit([FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new CreateDeposit(deposit));
        return this.StatusResponseFromResult(result);
    }

    
}