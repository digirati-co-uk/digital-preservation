using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class UpdateDeposit(Deposit deposit) : IRequest<Result<Deposit?>>
{
    public Deposit Deposit { get; } = deposit;
}


public class UpdateDepositHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<UpdateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(UpdateDeposit request, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.UpdateDeposit(request.Deposit, cancellationToken);
        return result;
    }
}