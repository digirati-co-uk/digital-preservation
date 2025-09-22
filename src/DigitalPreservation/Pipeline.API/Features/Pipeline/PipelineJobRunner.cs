using DigitalPreservation.Common.Model.PipelineApi;
using MediatR;
using Pipeline.API.Features.Pipeline.Requests;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobRunner(
    ILogger<PipelineJobRunner> logger,
    IMediator mediator)
{
    //TODO: return status result
    public async Task Execute(PipelineJobMessage jobIdAndDepositName, CancellationToken cancellationToken)
    {
        var jobId = jobIdAndDepositName.JobIdentifier;
        var depositId = jobIdAndDepositName.DepositName;
        var runUser = jobIdAndDepositName.RunUser;

        if (string.IsNullOrEmpty(jobId))
        {
            logger.LogError($"Job id is null execute pipeline job for the deposit {depositId} and job id {jobId}");
            return;
        }

        try
        {
            logger.LogInformation($"Sending execute pipeline job for the deposit {depositId} and job id {jobId}");

            var executeResult = await mediator.Send(new ExecutePipelineJob(jobId, depositId, runUser), cancellationToken);

            if (executeResult.Success)
            {
                logger.LogInformation($"Successfully sent execute pipeline job for the deposit {depositId} and job id {jobId} ");
                return;

            }

            logger.LogError($"Could not successfully send execute pipeline job for the deposit {depositId} and job id {jobId} because of {executeResult.ErrorMessage}");
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error execute pipeline job for the deposit {depositId} and job id {jobId}");
        }
    }
}