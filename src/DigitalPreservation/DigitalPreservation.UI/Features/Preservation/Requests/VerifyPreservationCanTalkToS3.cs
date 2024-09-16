using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class VerifyPreservationCanTalkToS3 : IRequest<bool>
{
}

public class VerifyPreservationCanTalkToS3Handler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<VerifyPreservationCanTalkToS3, bool>
{
    public Task<bool> Handle(VerifyPreservationCanTalkToS3 request, CancellationToken cancellationToken)
        => preservationApiClient.CanTalkToS3(cancellationToken);
}
