using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
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
    
    
    [HttpGet("{id}/mets", Name = "GetDepositWithMets")]
    [ProducesResponseType<List<Deposit>>(200, "application/xml")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDepositMets([FromRoute] string id)
    {
        var wrapper = await mediator.Send(new GetDepositWithMets(id));
        if (wrapper is
            {
                Success: true, 
                Value: not null, 
                Value.MetsFileWrapper: not null, 
                Value.MetsFileWrapper.XDocument: not null
            })
        {
            Response.Headers.ETag = wrapper.Value.MetsFileWrapper.ETag;
            return Content(wrapper.Value.MetsFileWrapper.XDocument!.ToString(), "application/xml");
        }
        return this.StatusResponseFromResult(wrapper);
    }
    
        
    [HttpPatch("{id}", Name = "PatchDeposit")]
    [ProducesResponseType<List<Deposit>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> PatchDeposit([FromRoute] string id, [FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new GetDeposit(id));
        if (result is { Success: true, Value: not null })
        {
            var existingDeposit = result.Value;
            deposit.Id = existingDeposit.Id;
            var patchResult = await mediator.Send(new PatchDeposit(deposit));
            return this.StatusResponseFromResult(patchResult);
        }
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
        var result = await mediator.Send(new CreateDeposit(deposit, false));
        Uri? createdLocation = null;
        if (result.Success)
        {
            createdLocation = new Uri($"/deposits/{result.Value!.Id!.GetSlug()}", UriKind.Relative);
        }
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    [HttpPost("export", Name = "Export")]
    [ProducesResponseType<List<Deposit>>(201, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ExportArchivalGroup([FromBody] Deposit deposit)
    {
        var result = await mediator.Send(new CreateDeposit(deposit, true));
        Uri? createdLocation = null;
        if (result.Success)
        {
            createdLocation = new Uri($"/deposits/{result.Value!.Id!.GetSlug()}", UriKind.Relative);
        }
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    
    


    
}