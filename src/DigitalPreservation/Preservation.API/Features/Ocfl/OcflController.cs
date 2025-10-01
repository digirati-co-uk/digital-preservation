using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Ocfl.Requests;

namespace Preservation.API.Features.Ocfl;

[Route("ocfl")]
[ApiController]
public class OcflController(IMediator mediator) : Controller
{

    [HttpGet("storagemap/{*path}", Name = "GetStorageMap")]
    [ProducesResponseType<StorageMap>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetStorageMap(
        [FromRoute] string path,
        [FromQuery] string? version = null,
        CancellationToken cancellationToken = default)
    {
        var mapResult = await mediator.Send(new GetStorageMap(path, version), cancellationToken);
        return this.StatusResponseFromResult(mapResult);
    }
    
}