using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Agents.Requests;

namespace Preservation.API.Features.Agents;

[Route("[controller]")]
[ApiController]
public class AgentsController(IMediator mediator) : Controller
{
    [HttpGet(Name = "ListAgents")]
    [ProducesResponseType<List<string>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ListAgents() // 
    {
        var result = await mediator.Send(new GetAgents());
        return this.StatusResponseFromResult(result);
    }
}