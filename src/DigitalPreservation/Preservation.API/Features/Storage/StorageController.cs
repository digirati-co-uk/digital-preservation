using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Storage.Requests;

namespace Preservation.API.Features.Storage;

/// <summary>
/// Temporary for connectivity check only
/// </summary>
[ApiController]
[Route("[controller]")]
public class StorageController(IMediator mediator) : Controller
{
    public async Task<IActionResult> StorageCheck()
    {
        var res = await mediator.Send(new VerifyStorageRunning());
        return new OkObjectResult(new { StorageAccessible = res });
    }
}