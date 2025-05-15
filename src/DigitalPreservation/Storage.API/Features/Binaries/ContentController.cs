using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Repository.Requests;
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
        var result = await mediator.Send(new GetResourceFromFedora(path));
        if (result.Success)
        {
            var resource = result.Value;
            if (resource is Binary binary)
            {
                var streamResult = await storage.GetStream(binary.Origin!);
                if (streamResult is { Success: true, Value: not null })
                {
                    return new FileStreamResult(streamResult.Value, binary.ContentType!);
                }

                var pdr = streamResult.ToProblemDetails("Cannot stream content");
                return new ObjectResult(pdr);
            }

            var pd = new ProblemDetails
            {
                Status = 404,
                Detail = "No binary at path " + path,
                Title = "Not Found"
            };
            return new ObjectResult(pd);
        }
        return this.StatusResponseFromResult(result);
    }
    
}