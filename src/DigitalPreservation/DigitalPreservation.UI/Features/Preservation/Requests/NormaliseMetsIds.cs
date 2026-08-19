using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class NormaliseMetsIds(string id) : IRequest<Result<MetsIdNormalisationReport>>
{
    public string Id { get; } = id;
}

public class NormaliseMetsIdsHandler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<NormaliseMetsIds, Result<MetsIdNormalisationReport>>
{
    public async Task<Result<MetsIdNormalisationReport>> Handle(
        NormaliseMetsIds request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.NormaliseDepositMetsIds(request.Id, cancellationToken);
    }
}
