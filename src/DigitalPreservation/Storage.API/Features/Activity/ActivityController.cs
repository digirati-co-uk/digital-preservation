using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Activity.Requests;

namespace Storage.API.Features.Activity;

[Route("[controller]")]
[ApiController]
public class ActivityController(IMediator mediator) : Controller
{
    [HttpGet("importjobs/collection", Name = "GetImportJobsCollection")]
    public async Task<IActionResult> GetImportJobsCollection()
    {
        var result = await mediator.Send(new GetImportJobsOrderedCollection());
        return this.StatusResponseFromResult(result);
    }


    [HttpGet("importjobs/pages/{page}", Name = "GetImportJobsPage")]
    public async Task<IActionResult> GetImportJobsPage(int page)
    {
        var result = await mediator.Send(new GetImportJobsOrderedCollectionPage(page));
        return this.StatusResponseFromResult(result);
    }
}