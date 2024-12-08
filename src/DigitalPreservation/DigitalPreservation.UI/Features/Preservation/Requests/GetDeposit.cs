using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetDeposit(string id) : IRequest<Result<Deposit?>>
{
    public string Id { get; } = id;
}

public class GetDepositHandler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<GetDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(GetDeposit request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetDeposit(request.Id, cancellationToken);
    }
}