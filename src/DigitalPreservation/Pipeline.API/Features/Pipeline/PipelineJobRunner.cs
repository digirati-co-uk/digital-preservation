using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using MediatR;
using Pipeline.API.Features.Pipeline.Requests;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobRunner(
    ILogger<PipelineJobRunner> logger,
    IMediator mediator)
{
    public async Task Execute(string depositName, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Sending execute pipeline job for the deposit");
            var executeResult = await mediator.Send(new ExecutePipelineJob(depositName, new ProcessPipelineResult { Status = "processing" }), cancellationToken);

            if (executeResult.Success)
            {
                logger.LogInformation("Successfully sent execute pipeline job for the deposit");
                return;
            }
        }
        catch (Exception e)
        { 
            var s = e.Message;
        }

        logger.LogError("Unable to execute pipeline runner job: " );
    }
}