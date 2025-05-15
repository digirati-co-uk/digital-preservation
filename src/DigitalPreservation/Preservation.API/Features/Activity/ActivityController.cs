using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Activity.Requests;

namespace Preservation.API.Features.Activity;

[Route("[controller]")]
[ApiController]
public class ActivityController(IMediator mediator) : Controller
{
    [HttpGet("archivalgroups/collection", Name = "GetArchivalGroupsCollection")]
    public async Task<IActionResult> GetArchivalGroupsCollection()
    {
        var result = await mediator.Send(new GetArchivalGroupsOrderedCollection());
        return this.StatusResponseFromResult(result);
    }

    [HttpGet("archivalgroups/pages/{page}", Name = "GetArchivalGroupsPage")]
    public async Task<IActionResult> GetArchivalGroupsPage(int page)
    {
        var result = await mediator.Send(new GetArchivalGroupsOrderedCollectionPage(page));
        return this.StatusResponseFromResult(result);
    }
    

    [HttpPost("archivalgroups/collection", Name = "PushArchivalGroupUpdate")]
    public async Task<IActionResult> PushEventToStream([FromBody] DigitalPreservation.Common.Model.ChangeDiscovery.Activity? activity)
    {
        var result = await mediator.Send(new PushArchivalGroupUpdate(activity));
        return this.StatusResponseFromResult(result, successStatusCode:204);
    }
}