using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage.Ocfl;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class RunPipeline(Deposit deposit, string runUser, string jobId, string depositId) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
    public string? RunUser { get; set; } = runUser;
    public string JobId { get; set; } = jobId;
    public string? DepositId { get; set; } = depositId;

}

public class RunPipelineHandler(IPreservationApiClient preservationApiClient, ILogger<RunPipelineHandler> logger) : IRequestHandler<RunPipeline, Result>
{
    public async Task<Result> Handle(RunPipeline request, CancellationToken cancellationToken)
    {
        var pipelineDeposit = new PipelineDeposit
        {
            Id = request.JobId,
            Status = PipelineJobStates.Waiting,
            DepositId = request.DepositId,
            RunUser = request.RunUser
        };
        
        var logResult = await preservationApiClient.LogPipelineRunStatus(pipelineDeposit, cancellationToken);

        if(logResult.Failure)
            logger.LogError("Failed to log the waiting status for jobId {jobId} for deposit {depositId}", request.JobId, request.DepositId);
        return await preservationApiClient.RunPipeline(request.Deposit, request.RunUser, request.JobId, cancellationToken);
    }

}