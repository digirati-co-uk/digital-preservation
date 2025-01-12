using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetDeposits(DepositQuery? query) : IRequest<Result<DepositQueryPage>>
{
    // Ideally the Mediator Request would be the query class - but you need to pass it to the API Client
    public DepositQuery? Query { get; set; } = query;
}

public class GetDepositsForArchivalGroupHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetDeposits, Result<DepositQueryPage>>
{
    public async Task<Result<DepositQueryPage>> Handle(GetDeposits request, CancellationToken cancellationToken)
    {
        // request.Path is the AG path, need to mutate to 
        // Can't use a DBContext here, need to get from API because there are other clients too
        return await preservationApiClient.GetDeposits(request.Query, cancellationToken);
    }
}