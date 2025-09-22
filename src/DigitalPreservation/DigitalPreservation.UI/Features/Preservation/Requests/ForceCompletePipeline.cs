using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class ForceCompletePipeline(Deposit deposit, string runUser, string jobId, string depositId) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
    public string? RunUser { get; set; } = runUser;
    public string? JobId { get; set; } = jobId;
    public string? DepositId { get; set; } = depositId;
}

public class ForceCompletePipelineHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<ForceCompletePipeline, Result>
{
    public async Task<Result> Handle(ForceCompletePipeline request, CancellationToken cancellationToken)
    {
        if(string.IsNullOrEmpty(request.JobId))
            return Result.FailNotNull<Result>(ErrorCodes.UnknownError,
                $"Could not force the complete of deposit {request.DepositId} as pipeline run not started yet.");

        var pipelineDeposit = new PipelineDeposit
        {
            Id = request.JobId,
            Status = PipelineJobStates.CompletedWithErrors,
            DepositId = request.DepositId,
            RunUser = request.RunUser,
            Errors = "Forced completion of this pipeline run."
        };

        return await preservationApiClient.LogPipelineRunStatus(pipelineDeposit, cancellationToken);
    }

}