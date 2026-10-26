using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc;
using Preservation.Client;

namespace DigitalPreservation.UI.Controllers;

[Route("deposits/{id}")]
public class DepositMetsController(
    IPreservationApiClient preservationApiClient) : Controller
{
    /// <summary>
    /// I think only for debugging and diagnostics
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [Route("mets")]
    [HttpGet]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] string id, CancellationToken cancellationToken)
    {
        var metsResult = await preservationApiClient.GetMetsWithETag(id, cancellationToken);
        if (metsResult.Success)
        {
            Response.Headers.ETag = metsResult.Value.Item2;
            return Content(metsResult.Value.Item1, "application/xml");
        }
        return ControllerX.GetProblemObjectResult(metsResult);
    }
    
    /// <summary>
    /// Proxy the METS JSON - for instructional purposes!
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [Route("parsed-mets")]
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetParsed([FromRoute] string id, CancellationToken cancellationToken)
    {
        var parsedJsonResult = await preservationApiClient.GetParsedDepositMets(id, cancellationToken);
        if (parsedJsonResult.Success)
        {
            return Content(parsedJsonResult.Value!, "application/json");
        }
        return ControllerX.GetProblemObjectResult(parsedJsonResult);
    }
    
    /// <summary>
    /// Get a vanilla IIIF representation of the Deposit, suitable for taking into a Manifest Editor
    /// (The enhanced IIIF-P model is only suitable for a preserved deposit)
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [Route("iiif")]
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIIIF([FromRoute] string id, CancellationToken cancellationToken)
    {
        var parsedJsonResult = await preservationApiClient.GetIIIF(id, cancellationToken);
        if (parsedJsonResult.Success)
        {
            return Content(parsedJsonResult.Value!, "application/json");
        }
        return ControllerX.GetProblemObjectResult(parsedJsonResult);
    }
}

