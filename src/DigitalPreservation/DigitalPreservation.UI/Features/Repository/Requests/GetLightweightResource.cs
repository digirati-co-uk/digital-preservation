using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Repository.Requests;

public class GetLightweightResource(string pathUnderRoot, string? version) : IRequest<Result<PreservedResource?>>
{
    public string PathUnderRoot { get; } = pathUnderRoot;
    public string? Version { get; } = version;
}

public class GetLightweightResourceHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetLightweightResource, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetLightweightResource request, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.GetLightweightResource(request.PathUnderRoot, request.Version);
        return result;
    }
}