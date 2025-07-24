using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pipeline.API.Features.Pipeline.Requests;
using Pipeline.API.Middleware;

namespace Pipeline.API.Features.Pipeline;

[ApiKey]
[Route("[controller]")]
[ApiController]
public class PipelineController(IMediator mediator,
    ILogger<PipelineController> logger) : Controller
{

    [HttpPost(Name = "ExecutePipelineProcess")]
    [Produces<ProcessPipelineResult>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecutePipelineJob([FromBody] PipelineJob pipelineJob, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing pipeline process ");
        var pipelineProcessJobResult = await mediator.Send(new ProcessPipelineJob(pipelineJob), cancellationToken);
        logger.LogInformation("Returned from QueueImportJob");
        return this.StatusResponseFromResult(pipelineProcessJobResult, 204); //TODO: make this the S3 bucket location
    }
}
