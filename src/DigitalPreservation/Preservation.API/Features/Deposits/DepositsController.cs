using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;

namespace Preservation.API.Features.Deposits;


[Route("[controller]")]
[ApiController]
public class DepositsController(
    IMediator mediator,
    WorkspaceManagerFactory workspaceManagerFactory
    ) : Controller
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
    [ProducesResponseType<Deposit>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetDeposit([FromRoute] string id)
    {
        var result = await mediator.Send(new GetDeposit(id));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpGet("{id}/mets", Name = "GetDepositWithMets")]
    [ProducesResponseType(200)]
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



    [HttpPost("{id}/mets", Name = "AddDepositItemsToMets")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> AddItemsToMets([FromRoute] string id, [FromBody] List<WorkingBase> items)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult is { Success: true, Value: not null })
        {
            var deposit = depositResult.Value;
            var eTag = Request.Headers.IfMatch.FirstOrDefault();
            if (eTag.HasText() && eTag == deposit.MetsETag)
            {
                var workspaceManager = workspaceManagerFactory.Create(depositResult.Value);
                var addResult = await workspaceManager.AddItemsToMets(items);
                return this.StatusResponseFromResult(addResult);
            }

            var pd = new ProblemDetails
            {
                Title = "Conflict: ETag does not match deposit METS",
                Detail = deposit.MetsETag,
                Status = 409
            };
            return Conflict(pd);
        }
        return this.StatusResponseFromResult(depositResult);
    }
    
    
    [HttpPost("{id}/mets/delete", Name = "DeleteFromDeposit")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> DeleteItemsToMets([FromRoute] string id, [FromBody] DeleteSelection deleteSelection)
    {
        var depositResult = await mediator.Send(new GetDeposit(id));
        if (depositResult is { Success: true, Value: not null })
        {
            var deposit = depositResult.Value;
            var eTag = Request.Headers.IfMatch.FirstOrDefault();
            if (eTag.HasText() && eTag == deposit.MetsETag)
            {
                var workspaceManager = workspaceManagerFactory.Create(depositResult.Value);
                var deleteResult = await workspaceManager.DeleteItems(deleteSelection);
                return this.StatusResponseFromResult(deleteResult);
            }

            var pd = new ProblemDetails
            {
                Title = "Conflict: ETag does not match deposit METS",
                Detail = deposit.MetsETag,
                Status = 409
            };
            return Conflict(pd);
        }
        return this.StatusResponseFromResult(depositResult);
    }
    
        
    [HttpPatch("{id}", Name = "PatchDeposit")]
    [ProducesResponseType<Deposit>(200, "application/json")]
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
    [ProducesResponseType<Deposit>(201, "application/json")]
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
    [ProducesResponseType<Deposit>(201, "application/json")]
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