using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Preservation.API.Features.Ocfl;

[Route("ocfl")]
[ApiController]
public class OcflController(IMediator mediator) : Controller
{

    [HttpGet("storagemap/{*path}", Name = "GetStorageMap")]
    [Produces<StorageMap>]
    [Produces("application/json")]
    public async Task<IActionResult> GetStorageMap(
        [FromRoute] string path,
        [FromQuery] string? version = null,
        CancellationToken cancellationToken = default)
    {
        var mapResult = await mediator.Send(new GetStorageMap(path, version), cancellationToken);
        return this.StatusResponseFromResult(mapResult);
    }
    
}