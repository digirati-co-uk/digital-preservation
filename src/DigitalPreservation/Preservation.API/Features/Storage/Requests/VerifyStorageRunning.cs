using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Storage.Requests;

/// <summary>
/// Call Storage API and verify communication. This is temporary only and will be removed once we have 'real' requests
/// implemented. It's a means to verify deployment only.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class VerifyStorageRunning : IRequest<bool>
{
}

public class VerifyStorageRunningHandler(IStorageApiClient storageApiClient)
    : IRequestHandler<VerifyStorageRunning, bool>
{
    public Task<bool> Handle(VerifyStorageRunning request, CancellationToken cancellationToken)
        => storageApiClient.IsAlive(cancellationToken);
}