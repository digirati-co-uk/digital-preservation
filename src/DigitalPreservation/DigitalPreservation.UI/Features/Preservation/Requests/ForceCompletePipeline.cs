using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class ForceCompletePipeline(string jobId, string depositId, ClaimsPrincipal user) : IRequest<Result>
{
    public string? JobId { get; set; } = jobId;
    public string? DepositId { get; set; } = depositId;
    public ClaimsPrincipal User { get; } = user;
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
            Errors = request.User.GetCallerIdentity() +  " forced completion of this pipeline run."
        };

        return await preservationApiClient.LogPipelineRunStatus(pipelineDeposit, cancellationToken);
    }

}