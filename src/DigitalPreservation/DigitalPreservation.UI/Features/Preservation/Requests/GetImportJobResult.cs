using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetImportJobResult(string depositId, string importJobId) : IRequest<Result<ImportJobResult>>
{
    public string DepositId { get; } = depositId;
    public string ImportJobId { get; } = importJobId;
}

public class GetImportJobResultHandler(
    IPreservationApiClient preservationApiClient) : IRequestHandler<GetImportJobResult, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(GetImportJobResult request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetImportJobResult(request.DepositId, request.ImportJobId, cancellationToken);
    }
}