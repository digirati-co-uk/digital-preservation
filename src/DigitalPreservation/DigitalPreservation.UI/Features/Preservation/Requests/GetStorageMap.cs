using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using MediatR;
using Preservation.Client;

namespace DigitalPreservation.UI.Features.Preservation.Requests;

public class GetStorageMap(string pathUnderRoot, string? version) : IRequest<Result<StorageMap>>
{
    public string PathUnderRoot { get; } = pathUnderRoot;
    public string? Version { get; } = version;
}

public class GetStorageMapHandler(IPreservationApiClient preservationApiClient) : IRequestHandler<GetStorageMap, Result<StorageMap>>
{
    public async Task<Result<StorageMap>> Handle(GetStorageMap request, CancellationToken cancellationToken)
    {
        var result = await preservationApiClient.GetStorageMap(request.PathUnderRoot, request.Version);
        return result;
    }
}