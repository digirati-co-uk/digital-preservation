using MediatR;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.Storage.Requests;

/// <summary>
/// Call Storage API and verify communication. This is temporary only and will be removed once we have 'real' requests
/// implemented. It's a means to verify deployment only.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class VerifyStorageRunning : IRequest<ConnectivityCheckResult>
{
}

public class VerifyStorageRunningHandler(IStorageApiClient storageApiClient)
    : IRequestHandler<VerifyStorageRunning, ConnectivityCheckResult?>
{
    public Task<ConnectivityCheckResult?> Handle(VerifyStorageRunning request, CancellationToken cancellationToken)
        => storageApiClient.IsAlive(cancellationToken);
}