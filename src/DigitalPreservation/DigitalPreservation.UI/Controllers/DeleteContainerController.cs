using DigitalPreservation.Common.Model;
using DigitalPreservation.UI.Features.Repository.Requests;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.UI.Controllers;

[Route("deletecontainer")]
public class DeleteContainerController(IMediator mediator) : Controller
{
    [HttpPost]
    public async Task<ActionResult> DeleteContainer([FromForm] string pathUnderRoot, [FromForm] bool? purge = null)
    {
        bool purgeCheck = purge ?? false;
        var result = await mediator.Send(new DeleteContainer(pathUnderRoot, purgeCheck));
        if (result.Success)
        {
            TempData["ContainerSuccess"] = $"Container {pathUnderRoot} deleted successfully";
            return Redirect($"/browse/{pathUnderRoot.GetParent()}");
        }
        TempData["ContainerError"] = $"Container {pathUnderRoot} could not be deleted: {result.CodeAndMessage()}";
        return Redirect($"/browse/{pathUnderRoot}");
    }
    
}