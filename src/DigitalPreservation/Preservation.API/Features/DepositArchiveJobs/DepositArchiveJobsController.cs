using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.DepositArchiveJobs.Requests;
using Preservation.API.Features.PipelineRunJobs.Requests;


namespace Preservation.API.Features.DepositArchiveJobs;

[Route("[controller]")]
[ApiController]
public class DepositArchiveJobsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id}", Name = "GetArchiveJobResult")]
    [ProducesResponseType<ArchiveJobResult>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetArchiveJobResult([FromRoute] string id)
    {
        var result = await mediator.Send(new GetArchiveJobResultForDeposit(id));

        if (result.Success)
            return Ok(result.Value);

        return StatusCode(
            result.ErrorCode switch
            {
                ErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest
            },
            new ProblemDetails
            {
                Title = result.ErrorCode,
                Detail = result.ErrorMessage
            });
    }

}
