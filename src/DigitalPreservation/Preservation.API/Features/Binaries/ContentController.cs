using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository.Requests;
using Storage.Repository.Common;

namespace Preservation.API.Features.Binaries;

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
        var repositoryPath = StringUtils.BuildPath(
            true, PreservedResource.BasePathElement, path);
        var result = await mediator.Send(new GetResource(repositoryPath));
        if (result.Success)
        {
            var resource = result.Value;
            if (resource is Binary binary)
            {
                var stream = await storage.GetStream(binary.Origin);
                return new FileStreamResult(stream, binary.ContentType!);
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