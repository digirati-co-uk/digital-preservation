using DigitalPreservation.Common.Model;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;
using Preservation.Client;

namespace DigitalPreservation.UI.Controllers;

[Route("[controller]/{*path}")]
public class BinaryController(
    IPreservationApiClient preservationApiClient) : Controller
{
    public async Task<IActionResult> Get([FromRoute] string path)
    {
        var repositoryPath = StringUtils.BuildPath(
            true, PreservedResource.BasePathElement, path);
        var streamWithContentType = await preservationApiClient.GetContentStream(repositoryPath, CancellationToken.None);
        if (streamWithContentType is { Item1: not null, Item2: not null })
        {
            return new FileStreamResult(streamWithContentType.Item1, streamWithContentType.Item2);
        }
        
        var pd = new ProblemDetails
        {
            Status = 500,
            Detail = "Cannot stream binary file",
            Title = "Error"
        };
        return new ObjectResult(pd);
    }
}