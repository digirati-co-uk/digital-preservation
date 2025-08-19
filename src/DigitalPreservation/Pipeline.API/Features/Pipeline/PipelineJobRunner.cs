using DigitalPreservation.Common.Model.PipelineApi;
using MediatR;
using Pipeline.API.Features.Pipeline.Requests;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobRunner(
    ILogger<PipelineJobRunner> logger,
    IMediator mediator,
    IPipelineJobStateLogger pipelineJobStateLogger)
{
    public async Task Execute(PipelineJobMessage jobIdAndDepositName, CancellationToken cancellationToken) //job identifier goes in here
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
            await pipelineJobStateLogger.LogJobState(jobId, depositId, runUser, PipelineJobStates.Waiting);
            var executeResult = await mediator.Send(new ExecutePipelineJob(jobId, depositId, runUser), cancellationToken);

            if (executeResult.Success)
            {
                logger.LogInformation($"Successfully sent execute pipeline job for the deposit {depositId} and job id {jobId}");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error execute pipeline job for the deposit {depositId} and job id {jobId}");
        }
    }
}