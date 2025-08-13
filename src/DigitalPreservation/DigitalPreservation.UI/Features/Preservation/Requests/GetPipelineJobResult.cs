using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetPipelineJobResult(string depositId, string pipelineJobId) : IRequest<Result<ProcessPipelineResult>>
{
    public string DepositId { get; } = depositId;
    public string PipelineJobId { get; } = pipelineJobId;
}

public class GetPipelineJobResultHandler(
    IPreservationApiClient preservationApiClient) : IRequestHandler<GetPipelineJobResult, Result<ProcessPipelineResult>>
{
    public async Task<Result<ProcessPipelineResult>> Handle(GetPipelineJobResult request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetPipelineJobResult(request.DepositId, request.PipelineJobId, cancellationToken);
    }
}