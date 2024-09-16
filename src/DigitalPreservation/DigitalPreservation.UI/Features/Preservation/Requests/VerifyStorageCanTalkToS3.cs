using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class VerifyStorageCanTalkToS3 : IRequest<bool>
{
}

public class VerifyStorageCanTalkToS3Handler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<VerifyStorageCanTalkToS3, bool>
{
    public Task<bool> Handle(VerifyStorageCanTalkToS3 request, CancellationToken cancellationToken)
        => preservationApiClient.CanSeeThatStorageCanTalkToS3(cancellationToken);
}