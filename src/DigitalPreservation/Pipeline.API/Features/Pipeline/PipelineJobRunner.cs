using MediatR;
using Pipeline.API.Features.Pipeline.Requests;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobRunner(
    ILogger<PipelineJobRunner> logger,
    IMediator mediator)
{
    public async Task Execute(PipelineJobMessage jobIdAndDepositName, CancellationToken cancellationToken)
    {
        var jobId = jobIdAndDepositName.JobIdentifier;
        var depositId = jobIdAndDepositName.DepositName;
        var runUser = jobIdAndDepositName.RunUser;

        if (string.IsNullOrEmpty(jobId))
        {
            logger.LogError("Job id is null execute pipeline job for the deposit {DepositId} and job id {JobId}", depositId, jobId);
            return;
        }

        try
        {
            logger.LogInformation("Sending execute pipeline job for the deposit {DepositId} and job id {JobId}", depositId, jobId);

            var executeResult = await mediator.Send(new ExecutePipelineJob(jobId, depositId, runUser), cancellationToken);

            if (executeResult.Success)
            {
                logger.LogInformation("Successfully sent execute pipeline job for the deposit {DepositId} and job id {JobId} ", depositId, jobId);
                return;

            }

            logger.LogError("Could not successfully send execute pipeline job for the deposit {DepositId} and job id {JobId} because of {ErrorMessage}", depositId, jobId, executeResult.ErrorMessage);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error execute pipeline job for the deposit {DepositId} and job id {JobId}", depositId, jobId);
        }
    }
}