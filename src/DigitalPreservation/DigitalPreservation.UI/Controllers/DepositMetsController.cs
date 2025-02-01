using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc;
using Preservation.Client;

namespace DigitalPreservation.UI.Controllers;

[Route("deposits/{id}/mets")]
public class DepositMetsController(
    IPreservationApiClient preservationApiClient) : Controller
{
    /// <summary>
    /// I think only for debugging and diagnostics
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] string id)
    {
        var metsResult = await preservationApiClient.GetMetsWithETag(id, CancellationToken.None);
        if (metsResult.Success)
        {
            Response.Headers.ETag = metsResult.Value.Item2;
            return Content(metsResult.Value.Item1, "application/xml");
        }
        return ControllerX.GetProblemObjectResult(metsResult);
    }
}