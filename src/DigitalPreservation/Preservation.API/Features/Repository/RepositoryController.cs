using DigitalPreservation.Common.Model;
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
    public async Task<IActionResult> Browse([FromRoute] string? path = null)
    {
        var res = await mediator.Send(new GetResource(Request.Path));
        return new OkObjectResult(res);
    }
}