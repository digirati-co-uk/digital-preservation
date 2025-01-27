using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Core.Web;
using DigitalPreservation.UI.Features.Preservation.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.UI.Controllers;

[Route("deposits/{id}/mets")]
public class DepositMetsController(
    IMediator mediator,
    IMetsParser metsParser) : Controller
{
    /// <summary>
    /// I think only for debugging and diagnostics
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet]
    [Produces("text/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] string id)
    {
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            var wrapper = await metsParser.GetMetsFileWrapper(getDepositResult.Value!.Files!);
            if (wrapper is { Success: true, Value: not null, Value.XDocument: not null })
            {
                return Content(wrapper.Value.XDocument!.ToString(), "text/xml");
            }

            return NotFound();
        }
        return ControllerX.GetProblemObjectResult(getDepositResult);
    }
}