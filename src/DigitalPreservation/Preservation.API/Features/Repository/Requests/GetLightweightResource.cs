using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class GetLightweightResource(string pathUnderRoot, string? version) : IRequest<Result<PreservedResource?>>
{
    public string PathUnderRoot { get; } = pathUnderRoot;
    public string? Version { get; } = version;
}

public class GetLightweightResourceHandler(IStorageApiClient storageApiClient) : IRequestHandler<GetLightweightResource, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetLightweightResource request, CancellationToken cancellationToken)
    {
        var result = await storageApiClient.GetLightweightResource(request.PathUnderRoot, request.Version);
        return result;
    }
}