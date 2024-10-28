using DigitalPreservation.Core.Web;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.S3;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.UI.Controllers;

[Route("deposits/{id}/metslike")]
public class DepositMetsLikeController(IMediator mediator) : Controller
{
    /// <summary>
    /// I think only for debugging and diagnostics
    /// </summary>
    /// <param name="id"></param>
    /// <param name="readS3"></param>
    /// <returns></returns>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] string id, [FromQuery] bool readS3 = false)
    {
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            var readS3Result = await mediator.Send(new GetWorkingDirectory(
                getDepositResult.Value!.Files!, readS3, false));
            if (readS3Result.Success)
            {
                return Json(readS3Result.Value);
            }
            return new ObjectResult(readS3Result.ToProblemDetails());
        }
        return new ObjectResult(getDepositResult.ToProblemDetails());
    }
}