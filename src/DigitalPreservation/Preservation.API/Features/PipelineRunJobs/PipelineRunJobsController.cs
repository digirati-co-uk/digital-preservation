using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.ImportJobs;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Features.PipelineRunJobs.Requests;

namespace Preservation.API.Features.PipelineRunJobs;

[Route("deposits/{depositId}/[controller]")]
[ApiController]
public class PipelineRunJobsController(IMediator mediator) : Controller
{

    [HttpGet("results", Name = "GetPipelineJobResults")]
    [ProducesResponseType<List<ProcessPipelineResult>>(200, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetPipelineJobResults([FromRoute] string depositId)
    {
        var result = await mediator.Send(new GetPipelineJobResultsForDeposit(depositId));
        return this.StatusResponseFromResult(result);
    }
}
