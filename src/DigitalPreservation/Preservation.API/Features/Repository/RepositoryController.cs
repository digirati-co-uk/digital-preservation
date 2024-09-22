using DigitalPreservation.Common.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository.Requests;

namespace Preservation.API.Features.Repository;

[Route("[controller]/{*path}")]
[ApiController]
public class RepositoryController(IMediator mediator) : Controller
{
    [HttpGet(Name = "Browse")]
    [ProducesResponseType<Container>(200, "application/json")]
    [ProducesResponseType<Binary>(200, "application/json")]
    // and also AG or DigitalObject
    public async Task<IActionResult> Browse([FromRoute] string path)
    {
        var res = await mediator.Send(new GetResource(path));
        return new OkObjectResult(res);
    }
}