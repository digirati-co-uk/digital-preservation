using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class SendDiffImportJob(string depositId, bool suppressActivityStreamEvent = false)
    : IRequest<Result<ImportJobResult>>
{
    public string DepositId { get; } = depositId;

    /// <summary>
    /// Keep the version this creates out of the published Activity Stream. Offered in the UI only
    /// for the METS ID migration, which changes how an object is recorded rather than what it holds.
    /// </summary>
    public bool SuppressActivityStreamEvent { get; } = suppressActivityStreamEvent;
}

public class SendDiffImportJobHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<SendDiffImportJob, Result<ImportJobResult>>
{
    public Task<Result<ImportJobResult>> Handle(SendDiffImportJob request, CancellationToken cancellationToken)
    {
        return preservationApiClient.SendDiffImportJob(
            request.DepositId, request.SuppressActivityStreamEvent, cancellationToken);
    }
}
