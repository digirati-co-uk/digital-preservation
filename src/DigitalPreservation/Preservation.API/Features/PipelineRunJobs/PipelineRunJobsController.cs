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
public class PipelineRunJobsController(ILogger<ImportJobsController> logger,
    IMediator mediator) : Controller
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


    /// <summary>
    /// Get the status of an existing PipelineJobResult - the result of executing an PipelineRunJob
    /// </summary>
    /// <param name="depositId">Deposit depositId import job is for</param>
    /// <param name="depositJobId">Unique deposit job identifier</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Status of ImportJobResult</returns>
    [HttpGet("results/{depositJobId}")]
    public async Task<IActionResult> GetImportJobResult([FromRoute] string depositId, [FromRoute] string depositJobId,
        CancellationToken cancellationToken)
    {
        var importJobResultResult = await mediator.Send(new GetImportJobResult(depositId, depositJobId), cancellationToken);
        return this.StatusResponseFromResult(importJobResultResult);
    }
}
