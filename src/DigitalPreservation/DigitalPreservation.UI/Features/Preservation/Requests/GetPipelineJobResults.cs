using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetPipelineJobsResults(string depositId) : IRequest<Result<List<ProcessPipelineResult>>>
{
    public string DepositId { get; } = depositId;
}

public class GetPipelineJobsHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetPipelineJobsResults, Result<List<ProcessPipelineResult>>>
{
    public async Task<Result<List<ProcessPipelineResult>>> Handle(GetPipelineJobsResults request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetPipelineJobResultsForDeposit(request.DepositId, cancellationToken);
    }
}