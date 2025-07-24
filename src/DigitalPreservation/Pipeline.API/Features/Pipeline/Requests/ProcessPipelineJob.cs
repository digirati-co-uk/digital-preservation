using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ProcessPipelineJob(PipelineJob pipelineJob) : IRequest<Result<ProcessPipelineResult>>
{
    public PipelineJob PipelineProcessJob { get; } = pipelineJob;
}

public class ProcessPipelineHandler(
    ILogger<ProcessPipelineHandler> logger,
    IPipelineQueue pipelineQueue) : IRequestHandler<ProcessPipelineJob, Result<ProcessPipelineResult>>
{
    public async Task<Result<ProcessPipelineResult>> Handle(ProcessPipelineJob request, CancellationToken cancellationToken)
    {
        logger.LogInformation($"About to process pipeline request");
        try
        {
            if (string.IsNullOrEmpty(request.PipelineProcessJob.DepositName))
            {
                logger.LogError("Could not process pipeline request");
                return Result.FailNotNull<ProcessPipelineResult>(ErrorCodes.UnknownError, "Could not publish pipeline job as request ");
            }

            await pipelineQueue.QueueRequest(request.PipelineProcessJob.DepositName, cancellationToken);
            return await Task.FromResult(Result.OkNotNull(new ProcessPipelineResult
            {
                Status = "completed"
            }));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not process pipeline request");
            return Result.FailNotNull<ProcessPipelineResult>(ErrorCodes.UnknownError, "Could not publish pipeline job: " + e.Message);
        }

    }

}