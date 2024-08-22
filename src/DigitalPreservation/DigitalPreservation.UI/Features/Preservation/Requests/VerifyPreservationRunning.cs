using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

/// <summary>
/// Call Presrvation API and verify communication. This is temporary only and will be removed once we have 'real'
/// requests implemented. It's a means to verify deployment only.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class VerifyPreservationRunning : IRequest<bool>
{
}

public class VerifyPreservationRunningHandler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<VerifyPreservationRunning, bool>
{
    public Task<bool> Handle(VerifyPreservationRunning request, CancellationToken cancellationToken)
        => preservationApiClient.IsAlive(cancellationToken);
}