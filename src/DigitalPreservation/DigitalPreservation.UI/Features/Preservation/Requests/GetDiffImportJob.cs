using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetDiffImportJob(string depositId) : IRequest<Result<ImportJob>>
{
    public string DepositId { get; } = depositId;
}

public class GetDiffImportJobHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetDiffImportJob, Result<ImportJob>>
{
    public async Task<Result<ImportJob>> Handle(GetDiffImportJob request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetDiffImportJob(request.DepositId, cancellationToken);
    }
}