using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetImportJobResults(string depositId) : IRequest<Result<List<ImportJobResult>>>
{
    public string DepositId { get; } = depositId;
}

public class GetImportJobsHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetImportJobResults, Result<List<ImportJobResult>>>
{
    public async Task<Result<List<ImportJobResult>>> Handle(GetImportJobResults request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetImportJobResultsForDeposit(request.DepositId, cancellationToken);
    }
}