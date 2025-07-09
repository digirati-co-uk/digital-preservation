using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Repository.Requests;
using Storage.API.Fedora.Vocab;
using Storage.Repository.Common;

namespace Storage.API.Features.Binaries;

[ApiController]
[Route("[controller]/{*path}")]
public class ContentController(
    IMediator mediator,
    IStorage storage) : Controller
{
    [HttpGet(Name = "GetBinary")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetBinary([FromRoute] string path)
    {
        var typeResult = await mediator.Send(new GetResourceTypeFromFedora(path));
        if (typeResult is not { Success: true, Value: nameof(RepositoryTypes.Binary) })
        {
            // Avoids unnecessarily retrieving AGs or Containers
            return NotABinary();
        }

        var result = await mediator.Send(new GetResourceFromFedora(path));
        if (!result.Success)
        {
            return this.StatusResponseFromResult(result);
        }
        
        var resource = result.Value;
        if (resource is not Binary binary)
        {
            return NotABinary();
        }
        
        var streamResult = await storage.GetStream(binary.Origin!);
        if (streamResult is { Success: true, Value: not null })
        {
            return new FileStreamResult(streamResult.Value, binary.ContentType!);
        }

        var pdr = streamResult.ToProblemDetails("Cannot stream content");
        return new ObjectResult(pdr);

        
        IActionResult NotABinary()
        {
            var pd = new ProblemDetails
            {
                Status = 404,
                Detail = "No binary at path " + path,
                Title = "Not Found"
            };
            return new ObjectResult(pd);
        }
    }
    
}