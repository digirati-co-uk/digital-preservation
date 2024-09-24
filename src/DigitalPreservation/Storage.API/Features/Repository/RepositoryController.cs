using DigitalPreservation.Common.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Repository.Requests;

namespace Storage.API.Features.Repository;

[Route(PreservedResource.BasePathElement + "/{*path}")]
[ApiController]
public class RepositoryController(IMediator mediator) : Controller
{
    [HttpGet(Name = "Browse")]
    [ProducesResponseType<Container>(200, "application/json")]
    [ProducesResponseType<Binary>(200, "application/json")]
    [ProducesResponseType<ArchivalGroup>(200, "application/json")]
    public async Task<IActionResult> Browse([FromRoute] string? path)
    {
        var res = await mediator.Send(new GetResourceFromFedora(path));
        return new OkObjectResult(res);
    }
    
    
    [HttpPut(Name = "CreateContainer")]
    [ProducesResponseType<Container>(200, "application/json")]
    public async Task<ActionResult<Container?>> CreateContainer([FromRoute] string path, [FromBody] Container? container = null)
    {
        string? name = null;
        if (container != null)
        {
            name = container.Name;
        }
        var newContainer = await mediator.Send(new CreateContainerInFedora(path, name));
        return newContainer;
    }
}
