using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class ReleaseLock(Deposit deposit) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
}

public class ReleaseLockHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<ReleaseLock, Result>
{
    public async Task<Result> Handle(ReleaseLock request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.ReleaseDepositLock(request.Deposit, cancellationToken);
    }
}