using MediatR;
using Preservation.Client;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class VerifyStorageCanTalkToS3 : IRequest<ConnectivityCheckResult>
{
}

public class VerifyStorageCanTalkToS3Handler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<VerifyStorageCanTalkToS3, ConnectivityCheckResult?>
{
    public Task<ConnectivityCheckResult?> Handle(VerifyStorageCanTalkToS3 request, CancellationToken cancellationToken)
        => preservationApiClient.CanSeeThatStorageCanTalkToS3(cancellationToken);
}