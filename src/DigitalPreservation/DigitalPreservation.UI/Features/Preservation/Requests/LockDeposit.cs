using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class LockDeposit(Deposit deposit) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
}

public class LockDepositHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<LockDeposit, Result>
{
    public async Task<Result> Handle(LockDeposit request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.LockDeposit(request.Deposit, true, cancellationToken);
    }
}