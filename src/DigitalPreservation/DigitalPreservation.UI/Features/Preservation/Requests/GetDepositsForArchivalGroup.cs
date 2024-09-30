using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetDepositsForArchivalGroup(string depositsForArchivalGroupPath) : IRequest<Result<List<Deposit>>>
{
    public string Path { get; set; } = depositsForArchivalGroupPath;
}

public class GetDepositsForArchivalGroupHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetDepositsForArchivalGroup, Result<List<Deposit>>>
{
    public async Task<Result<List<Deposit>>> Handle(GetDepositsForArchivalGroup request, CancellationToken cancellationToken)
    {
        // request.Path is the AG path, need to mutate to 
        // Can't use a DBContext here, need to get from API because there are other clients too
        return await preservationApiClient.GetDepositsForArchivalGroup(request.Path);
    }
}