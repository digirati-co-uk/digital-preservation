using MediatR;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.Storage.Requests;

public class VerifyStorageCanSeeS3 : IRequest<ConnectivityCheckResult>
{
    
}

public class VerifyStorageCanSeeS3Handler(IStorageApiClient storageApiClient)
    : IRequestHandler<VerifyStorageCanSeeS3, ConnectivityCheckResult?>
{
    public Task<ConnectivityCheckResult?> Handle(VerifyStorageCanSeeS3 request, CancellationToken cancellationToken)
        => storageApiClient.CanSeeS3(cancellationToken);
}