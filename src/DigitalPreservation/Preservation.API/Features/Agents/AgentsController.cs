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
    [ProducesResponseType<List<Uri>>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> ListAgents() // 
    {
        var result = await mediator.Send(new GetAgents());
        return this.StatusResponseFromResult(result);
    }
}