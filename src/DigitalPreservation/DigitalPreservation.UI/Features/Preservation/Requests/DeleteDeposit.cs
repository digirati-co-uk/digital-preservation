using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class DeleteDeposit(string id) : IRequest<Result>
{
    public string Id { get; } = id;
}

public class DeleteDepositHandler(IPreservationApiClient preservationApiClient)
    : IRequestHandler<DeleteDeposit, Result>
{
    public async Task<Result> Handle(DeleteDeposit request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.DeleteDeposit(request.Id, cancellationToken);
    }
}