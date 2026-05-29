using DigitalPreservation.Core.Web;
using IIIF.Serialisation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Iiif.Requests;

namespace Preservation.API.Features.Iiif;

[Route("iiif/{**path}")]
[ApiController]
public class IiifController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Returns a IIIF Manifest for the Archival Group at the given path.
    /// The path must resolve to an Archival Group exactly — not a binary, container, or non-existent resource.
    /// Always requires authentication; there is no public/token variant.
    /// </summary>
    [HttpGet(Name = "GetArchivalGroupAsIIIFManifest")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetArchivalGroupAsIIIF([FromRoute] string? path)
    {
        if (string.IsNullOrEmpty(path))
            return NotFound();

        var root = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var manifestUrl = $"{root}/iiif/{path}";
        var iiifBaseUrl = $"{manifestUrl}/";

        var result = await mediator.Send(new GetArchivalGroupAsIIIFManifest(path, manifestUrl, iiifBaseUrl));
        if (result is { Success: true, Value: not null })
            return Content(result.Value.AsJson(), "application/json");

        return this.StatusResponseFromResult(result);
    }
}
