using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Headers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Repository.Requests;
using Storage.API.Fedora.Vocab;

namespace Storage.API.Features.Repository;

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
    public async Task<IActionResult> Browse([FromRoute] string? path)
    {
       
        var result = await mediator.Send(new GetResourceFromFedora(path));
        return this.StatusResponseFromResult(result);
    }
    
    
    [HttpHead(Name = "HeadResource")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult?> HeadResource([FromRoute] string path)
    {
        var result = await mediator.Send(new GetResourceTypeFromFedora(path));
        if (result.Success)
        {
            if (result.Value == nameof(RepositoryTypes.Tombstone))
            {
                Response.StatusCode = 410;
            }
            else
            {
                Response.Headers[HttpHeaders.XPreservationResourceType] = result.Value;
            }
        }
        else
        {
            Response.StatusCode = result.ToProblemDetails().Status ?? 500;
        }
        return new EmptyResult();
    }
    
    
    [HttpPut(Name = "CreateContainer")]
    [ProducesResponseType<Container>(201, "application/json")]
    [ProducesResponseType(401)]
    [ProducesResponseType(409)]
    [ProducesResponseType(400)]
    public async Task<ActionResult> CreateContainer([FromRoute] string path, [FromBody] Container? container = null)
    {
        string? name = null;
        if (container != null)
        {
            name = container.Name;
        }
        var result = await mediator.Send(new CreateContainerInFedora(path, User.GetCallerIdentity(), name));
        var createdLocation = result.Success ? result.Value!.Id : null;
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }
    
    
    [HttpDelete(Name = "DeleteContainer")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(410)]
    public async Task<ActionResult> DeleteContainer([FromRoute] string path, [FromQuery] bool purge)
    {
        var result = await mediator.Send(new DeleteContainerFromFedora(path, User.GetCallerIdentity(), purge));
        return this.StatusResponseFromResult(result, 204);
    }
}
