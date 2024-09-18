using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Fedora.Requests;

namespace Storage.API.Features.Fedora;

/// <summary>
/// Temporary for connectivity check only
/// </summary>
[ApiController]
[Route("[controller]")]
public class FedoraController(IMediator mediator) : Controller
{
    public async Task<IActionResult> FedoraCheck()
    {
        var res = await mediator.Send(new VerifyFedoraRunning());
        return new OkObjectResult(res);
    }
}