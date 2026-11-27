using DigitalPreservation.Core.Web;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
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
    /// Send the browser to the Deposit's IIIF Manifest at its tokenised address on the Preservation
    /// API (issue #261), so the address bar holds a URL a IIIF viewer can load without signing in.
    /// The Preservation API is on the private load balancer, so it only opens from the staff network.
    /// </summary>
    [Route("iiif-manifest")]
    [HttpGet]
    public async Task<IActionResult> GetIIIFManifest([FromRoute] string id, CancellationToken cancellationToken)
    {
        var uriResult = await preservationApiClient.GetIIIFManifestUri(id, cancellationToken);
        if (uriResult.Success)
        {
            return Redirect(uriResult.Value!.AbsoluteUri);
        }
        if (uriResult.ErrorCode == ErrorCodes.NotFound)
        {
            // The feature flag answers 404 when the Deposit IIIF routes are switched off.
            return ControllerX.GetProblemObjectResult(Result.Fail(ErrorCodes.NotFound,
                "No IIIF Manifest is available for this deposit. The Deposit IIIF view may be switched off on this instance (EnableIiifMediaEndpoints)."));
        }
        return ControllerX.GetProblemObjectResult(uriResult);
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

