using MediatR;
using Storage.API.Fedora;
using Storage.Repository.Common;

namespace Storage.API.Features.Fedora.Requests;

/// <summary>
/// Call Storage API and verify communication. This is temporary only and will be removed once we have 'real' requests
/// implemented. It's a means to verify deployment only.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class VerifyFedoraRunning : IRequest<ConnectivityCheckResult>
{
}

public class VerifyFedoraRunningHandler(IFedoraClient fedoraClient)
    : IRequestHandler<VerifyFedoraRunning, ConnectivityCheckResult>
{
    public Task<ConnectivityCheckResult> Handle(VerifyFedoraRunning request, CancellationToken cancellationToken)
        => fedoraClient.IsAlive(cancellationToken);
}