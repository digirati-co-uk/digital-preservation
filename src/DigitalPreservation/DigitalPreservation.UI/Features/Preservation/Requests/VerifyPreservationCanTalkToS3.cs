using MediatR;
using Preservation.Client;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class VerifyPreservationCanTalkToS3 : IRequest<ConnectivityCheckResult>
{
}

public class VerifyPreservationCanTalkToS3Handler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<VerifyPreservationCanTalkToS3, ConnectivityCheckResult?>
{
    public Task<ConnectivityCheckResult?> Handle(VerifyPreservationCanTalkToS3 request, CancellationToken cancellationToken)
        => preservationApiClient.CanTalkToS3(cancellationToken);
}
