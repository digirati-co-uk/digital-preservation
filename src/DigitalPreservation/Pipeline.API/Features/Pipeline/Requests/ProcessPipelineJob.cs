using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ProcessPipelineJob(PipelineJob pipelineJob) : IRequest<Result>
{ 
    public PipelineJob PipelineProcessJob { get; } = pipelineJob;
}

public class ProcessPipelineHandler(
    ILogger<ProcessPipelineHandler> logger,
    IPipelineQueue pipelineQueue) : IRequestHandler<ProcessPipelineJob, Result>
{
    public async Task<Result> Handle(ProcessPipelineJob request, CancellationToken cancellationToken)
    {
        logger.LogInformation($"About to process pipeline request");
        var deposit = request.PipelineProcessJob.DepositName;
        var jobId = request.PipelineProcessJob.JobIdentifier;
        var runUser = request.PipelineProcessJob.RunUser;

        try
        {
            if (string.IsNullOrEmpty(deposit) || string.IsNullOrEmpty(jobId))
            {
                logger.LogError("Could not process pipeline request");
                return Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not publish pipeline job as request for job id {jobId} and deposit {deposit}");
            }

            logger.LogInformation($"About to queue pipeline request for job id {jobId} and deposit {deposit}");
            await pipelineQueue.QueueRequest(jobId, deposit, runUser, cancellationToken);

            return Result.Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Could not process pipeline request for job id {jobId} and deposit {deposit}");
            return Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not publish pipeline job for job id {jobId} and deposit {deposit}: " + e.Message);
        }

    }

}