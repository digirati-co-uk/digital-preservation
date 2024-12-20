using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetAllAgents : IRequest<Result<List<Uri>>>
{
    
}

public class GetAllAgentsHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetAllAgents, Result<List<Uri>>>
{
    public async Task<Result<List<Uri>>> Handle(GetAllAgents request, CancellationToken cancellationToken)
    {
        return await preservationApiClient.GetAllAgents(cancellationToken);
    }
}