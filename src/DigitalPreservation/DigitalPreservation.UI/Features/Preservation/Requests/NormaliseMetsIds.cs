using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class NormaliseMetsIds(string id, string? metsETag) : IRequest<Result<MetsIdNormalisationReport>>
{
    public string Id { get; } = id;

    /// <summary>
    /// The METS ETag the user was looking at when they asked. The API turns a mismatch into a
    /// 409 rather than silently overwriting whatever someone else saved in the meantime.
    /// </summary>
    public string? MetsETag { get; } = metsETag;
}

public class NormaliseMetsIdsHandler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<NormaliseMetsIds, Result<MetsIdNormalisationReport>>
{
    public async Task<Result<MetsIdNormalisationReport>> Handle(
        NormaliseMetsIds request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.NormaliseDepositMetsIds(
            request.Id, request.MetsETag, cancellationToken);
    }
}
