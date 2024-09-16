using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Storage.Requests;

public class VerifyStorageCanSeeS3 : IRequest<bool>
{
    
}

public class VerifyStorageCanSeeS3Handler(IStorageApiClient storageApiClient)
    : IRequestHandler<VerifyStorageCanSeeS3, bool>
{
    public Task<bool> Handle(VerifyStorageCanSeeS3 request, CancellationToken cancellationToken)
        => storageApiClient.CanSeeS3(cancellationToken);
}