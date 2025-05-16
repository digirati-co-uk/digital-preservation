using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storage.API.Features.Activity.Requests;

namespace Storage.API.Features.Activity;

[Route("[controller]")]
[ApiController]
public class ActivityController(ILogger<ActivityController> logger, IMediator mediator) : Controller
{
    [HttpGet("importjobs/collection", Name = "GetImportJobsCollection")]
    public async Task<IActionResult> GetImportJobsCollection()
    {
        logger.LogInformation("Received call for GetImportJobsCollection Activity Stream");
        var result = await mediator.Send(new GetImportJobsOrderedCollection());
        if (result.Failure)
        {
            logger.LogWarning(result.CodeAndMessage());
        }
        return this.StatusResponseFromResult(result);
    }


    [HttpGet("importjobs/pages/{page}", Name = "GetImportJobsPage")]
    public async Task<IActionResult> GetImportJobsPage(int page)
    {
        logger.LogInformation("Received call for GetImportJobsPage Activity Stream");
        var result = await mediator.Send(new GetImportJobsOrderedCollectionPage(page));
        if (result.Failure)
        {
            logger.LogWarning(result.CodeAndMessage());
        }
        return this.StatusResponseFromResult(result);
    }
}