using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using MediatR;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Ocfl;

public class GetStorageMap(string archivalGroupPathUnderRoot, string? version) : IRequest<Result<StorageMap>>
{
    public string ArchivalGroupPathUnderRoot { get; } = archivalGroupPathUnderRoot;
    public string? Version { get; } = version;
}

public class GetStorageMapHandler(
    ILogger<GetStorageMapHandler> logger,
    Converters converters,
    IStorageMapper storageMapper) : IRequestHandler<GetStorageMap, Result<StorageMap>>
{
    public async Task<Result<StorageMap>> Handle(GetStorageMap request, CancellationToken cancellationToken)
    {
        var uri = converters.RepositoryUriFromPathUnderRoot(request.ArchivalGroupPathUnderRoot);
        try
        {
            var map = await storageMapper.GetStorageMap(uri, request.Version);
            if (request.Version != null && map.Version.OcflVersion != request.Version)
            {
                return Result.FailNotNull<StorageMap>(ErrorCodes.UnknownError, 
                    "Returned storage map is version " + map.Version.OcflVersion + " but " + request.Version + " was requested.");
            }
            return Result.OkNotNull(map);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get storage map");
            return Result.FailNotNull<StorageMap>(ErrorCodes.UnknownError, 
                "Could not get storage map: " + e.Message);
        }
    }
}