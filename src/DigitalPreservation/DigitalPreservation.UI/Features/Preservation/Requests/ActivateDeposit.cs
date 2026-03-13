using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class ActivateDeposit(Deposit deposit) : IRequest<Result>
{
    public Deposit Deposit { get; } = deposit;
}

public class ActivateDepositHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<ActivateDeposit, Result>
{
    public async Task<Result> Handle(ActivateDeposit request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.ActivateDeposit(request.Deposit, cancellationToken);
    }
}
