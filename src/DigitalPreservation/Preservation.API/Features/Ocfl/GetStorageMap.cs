using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using MediatR;
using Storage.Client;

namespace Preservation.API.Features.Ocfl;

public class GetStorageMap(string archivalGroupPathUnderRoot, string? version) : IRequest<Result<StorageMap>>
{
    public string ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
    public string? Version { get; } = version;
}

public class GetStorageMapHandler(IStorageApiClient storageApiClient) : IRequestHandler<GetStorageMap, Result<StorageMap>>
{
    public async Task<Result<StorageMap>> Handle(GetStorageMap request, CancellationToken cancellationToken)
    {
        var result = await storageApiClient.GetStorageMap(request.ArchivalGroupPathUnderRoot, request.Version);
        return result;
    }
}