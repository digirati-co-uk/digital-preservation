using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace Pipeline.API.Features.Pipeline.Requests;

public class LogPipelineJobStatus(string depositId, string jobId, string status, string runUser) : IRequest<Result<LogPipelineStatusResult>>
{
    public string DepositId { get; } = depositId;
    public string JobId { get; set; } = jobId;
    public string Status { get; set; } = status;
    public string RunUser { get; set; } = runUser;
}

public class LogPipelineJobStatusHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<LogPipelineJobStatus, Result<LogPipelineStatusResult>>
{
    public async Task<Result<LogPipelineStatusResult>> Handle(LogPipelineJobStatus request, CancellationToken cancellationToken)
    {
        var pipelineDeposit = new PipelineDeposit
        {
            Id = request.JobId,
            Status = request.Status,
            DepositId = request.DepositId,
            RunUser = request.RunUser
        };
        return await preservationApiClient.LogPipelineRunStatus(pipelineDeposit, cancellationToken);
    }
}
