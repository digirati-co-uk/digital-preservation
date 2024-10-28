using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class SendDiffImportJob(string depositId) : IRequest<Result<ImportJobResult>>
{
    public string DepositId { get; } = depositId;
}

public class SendDiffImportJobHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<SendDiffImportJob, Result<ImportJobResult>>
{
    public Task<Result<ImportJobResult>> Handle(SendDiffImportJob request, CancellationToken cancellationToken)
    {
        return preservationApiClient.SendDiffImportJob(request.DepositId, cancellationToken);
    }
}