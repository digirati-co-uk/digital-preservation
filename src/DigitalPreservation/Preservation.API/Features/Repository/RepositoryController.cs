using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository.Requests;

namespace Preservation.API.Features.Repository;

[Route(PreservedResource.BasePathElement + "/{*path}")]
[ApiController]
public class RepositoryController(IMediator mediator) : Controller
{
    [HttpGet(Name = "Browse")]
    [ProducesResponseType<Container>(200, "application/json")]
    [ProducesResponseType<Binary>(200, "application/json")]
    [ProducesResponseType<ArchivalGroup>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Browse([FromRoute] string? path = null)
    {
        var result = await mediator.Send(new GetResource(Request.Path));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpPut(Name = "CreateContainer")]
    [ProducesResponseType<Container>(200, "application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(409)]
    [ProducesResponseType(400)]
    public async Task<ActionResult> CreateContainer([FromRoute] string? path = null, [FromBody] Container? container = null)
    {
        string? name = null;
        if (container != null)
        {
            name = container.Name;
        }
        var result = await mediator.Send(new CreateContainer(Request.Path, name));
        return this.StatusResponseFromResult(result);
    }
    
    
    
}