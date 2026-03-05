using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.DepositArchiveJobs.Requests;
using Preservation.API.Features.PipelineRunJobs.Requests;

namespace Preservation.API.Features.DepositArchiveJobs;

[Route("deposits/{depositId}/depositarchivejobs")]
[ApiController]
public class DepositArchiveJobsController(IMediator mediator) : Controller
{
    [HttpGet(Name = "GetArchiveJobResult")]
    [ProducesResponseType<List<ArchiveJobResult>>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetArchiveJobResults([FromRoute] string depositId)
    {
        var result = await mediator.Send(new GetArchiveJobResultForDeposit(depositId));
        return this.StatusResponseFromResult(result);
    }
}
