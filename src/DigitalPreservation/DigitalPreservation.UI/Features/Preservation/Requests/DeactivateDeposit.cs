using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;


public class DeactivateDeposit(Deposit deposit) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
}

public class DeactivateDepositHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<DeactivateDeposit, Result>
{
    public async Task<Result> Handle(DeactivateDeposit request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.DeactivateDeposit(request.Deposit, cancellationToken);
    }
}

