using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.DepositArchiveJobs.Requests;
using Preservation.API.Features.PipelineRunJobs.Requests;

namespace Preservation.API.Features.DepositArchiveJobs;

[Route("[controller]")]
[ApiController]
public class DepositArchiveJobsController(IMediator mediator) : Controller
{
    [HttpGet("{id}", Name = "GetArchiveJobResult")]
    [ProducesResponseType<List<ArchiveJobResult>>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetArchiveJobResult([FromRoute] string id)
    {
        var result = await mediator.Send(new GetArchiveJobResultForDeposit(id));
        return this.StatusResponseFromResult(result);
    }
}
