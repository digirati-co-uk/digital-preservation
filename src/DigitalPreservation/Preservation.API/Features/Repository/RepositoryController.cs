using System.Net;
using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository.Requests;
using DigitalPreservation.Mets;

namespace Preservation.API.Features.Repository;


[Route(PreservedResource.BasePathElement + "/{*path}")]
[ApiController]
public class RepositoryController(IMediator mediator, IMetsParser metsParser) : ControllerBase
{
    [HttpGet(Name = "Browse")]
    [ProducesResponseType<Container>(200, "application/json")]
    [ProducesResponseType<Binary>(200, "application/json")]
    [ProducesResponseType<ArchivalGroup>(200, "application/json")]
    [ProducesResponseType<MetsFileWrapper>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(410)]
    public async Task<IActionResult> Browse(
        [FromRoute] string? path = null,
        [FromQuery] string? view = null,
        [FromQuery] string? version = null)
    {
        if (version.HasText() && view != ViewValues.Lightweight)
        {
            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Version only supported on lightweight view",
                Detail = "View " + (view ?? "[empty]") + " is not a valid value when a version is requested."
            };
            return BadRequest(problem);
        }
        
        if (view == ViewValues.Lightweight)
        {
            var lwResult = await mediator.Send(new GetLightweightResource(path!, version));
            return this.StatusResponseFromResult(lwResult);
        }
        
        var result = await mediator.Send(new GetResource(Request.Path));
        if ((view is ViewValues.Mets or ViewValues.ParsedMets) && result is { Success: true, Value: ArchivalGroup archivalGroup })
        {
            return await GetMetsResult(archivalGroup, view);
        }
        
        // default 99.999% scenario:
        return this.StatusResponseFromResult(result);
    }

    private async Task<IActionResult> GetMetsResult(ArchivalGroup archivalGroup, string view)
    {            
        var mets = archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, true));
        if (mets is null)
        {
            mets = archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, false));
        }

        if (mets is null)
        {
            return NotFound();
        }
        
        // We need to stream the METS view from storage
        var streamResult = await mediator.Send(new GetBinaryStream(mets.Id!.AbsolutePath));
        if (streamResult is { Success: true, Value: not null })
        {
            // there is a METS file there to be read
            if (view == ViewValues.Mets)
            {
                return new FileStreamResult(streamResult.Value, "application/xml");
            }

            var xMets = XDocument.Load(streamResult.Value);
            var parsedMetsResult = metsParser.GetMetsFileWrapperFromXDocument(mets.Id, xMets);
            if (parsedMetsResult.Success)
            {
                parsedMetsResult.Value!.XDocument = null;
                return Ok(parsedMetsResult.Value);
            }

            return this.StatusResponseFromResult(parsedMetsResult);
        }

        return this.StatusResponseFromResult(streamResult);
    }


    [HttpHead(Name = "HeadResource")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(410)]
    public async Task<IActionResult?> HeadResource([FromRoute] string path)
    {
        var result = await mediator.Send(new GetResourceType(path));
        if (result.Success)
        {
            if (result.Value == nameof(ErrorCodes.Tombstone))
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
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    [ProducesResponseType<ProblemDetails>(409, "application/json")]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    public async Task<ActionResult> CreateContainer([FromRoute] string? path = null, [FromBody] Container? container = null)
    {
        string? name = null;
        if (container != null)
        {
            name = container.Name;
        }
        var result = await mediator.Send(new CreateContainer(Request.Path, name));
        var createdLocation = result.Success ? result.Value!.Id : null;
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }

    [HttpDelete(Name = "DeleteContainer")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(410, "application/json")]
    public async Task<ActionResult> DeleteContainer([FromRoute] string path, [FromQuery] bool purge)
    {
        var result = await mediator.Send(new DeleteContainer(Request.Path, purge));
        return this.StatusResponseFromResult(result, 204);
    }
}