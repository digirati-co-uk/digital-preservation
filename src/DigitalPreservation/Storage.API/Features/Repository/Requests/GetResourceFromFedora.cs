using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Storage.API.Fedora;

namespace Storage.API.Features.Repository.Requests;

/// <summary>
/// Returns the latest current version of the resource
/// </summary>
/// <param name="pathUnderFedoraRoot">The Fedora sub path, not including any fedora or storage URI prefix</param>
public class GetResourceFromFedora(string? pathUnderFedoraRoot) : IRequest<Result<PreservedResource?>>
{
    public string? PathUnderFedoraRoot { get; } = pathUnderFedoraRoot;
}

public class GetResourceFromFedoraHandler(
    ILogger<GetResourceFromFedoraHandler> logger,
    IMemoryCache memoryCache,
    IFedoraClient fedoraClient) : IRequestHandler<GetResourceFromFedora, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetResourceFromFedora request, CancellationToken cancellationToken)
    {
        // Only cache Archival Groups, and only then by version. Cache key must always include the version.
        // But we don't want to be checking everything to see if it's an AG (FedoraClient gets type later)
        // So we can also store a cached string of what version is cached, at the path.
        // We may not have been given a version in which case it's the latest one we want.
        // But we must never return a superseded version as the latest.
        
        // Is it in the cache?
        // If so, it must be an AG. What is its version in the cache?
        // This code is quite complex because we don't want to be getting versions, etc., for every resource,
        // ONLY when it's an Archival Group. But we don't know whether it's an Archival Group up front.

        // Do we have a token indicating that we have a version cached for the raw path?
        var possibleAgVersion = memoryCache.Get<string>(CacheKey(request.PathUnderFedoraRoot, null));
        if (possibleAgVersion.HasText())
        {
            logger.LogInformation("There is a cached token for {pathUnderFedoraRoot}: {version}",
                request.PathUnderFedoraRoot, possibleAgVersion);
            var possibleAg = memoryCache.Get<ArchivalGroup>(CacheKey(request.PathUnderFedoraRoot, possibleAgVersion));
            if (possibleAg != null)
            {
                logger.LogInformation("Retrieved cached Archival Group {pathUnderFedoraRoot} for cached version string {version}",
                    request.PathUnderFedoraRoot, possibleAgVersion);
                // It is HIGHLY LIKELY that this is the current version. But we can't guarantee it.
                // But we know it must be an archival group (we wouldn't have cached it otherwise).
                logger.LogInformation("Checking if this is indeed the HEAD version:");
                var versionResult = await fedoraClient.GetArchivalGroupVersion(request.PathUnderFedoraRoot);
                if (versionResult is { Success: true, Value: not null } &&
                    versionResult.Value.OcflVersion == possibleAgVersion)
                {
                    logger.LogInformation("HEAD version of {pathUnderFedoraRoot} is {version}",
                        request.PathUnderFedoraRoot, versionResult.Value.OcflVersion);
                    return Result.Ok<PreservedResource?>(possibleAg);
                }
                logger.LogWarning("Latest cached HEAD version value is {cachedVersion} but actual version is {actualVersion}",
                    possibleAgVersion, versionResult.Value?.OcflVersion);
            }
            logger.LogWarning("No Archival Group in cache for cached version string {pathUnderFedoraRoot} at path {version}",
                request.PathUnderFedoraRoot, possibleAgVersion);
        }
        
        // OK, so now we have to get the resource
        var result = await fedoraClient.GetResource(request.PathUnderFedoraRoot, cancellationToken: cancellationToken);

        if (result is { Success: true, Value.Type: nameof(ArchivalGroup) })
        {
            // We can cache this because it is an archival group. But it might not be the HEAD version
            var ag = result.Value as ArchivalGroup;
            var version = ag?.Version?.OcflVersion;
            if (ag != null && version.HasText())
            {
                var expiry = TimeSpan.FromHours(1);
                var versionedCacheKey = CacheKey(request.PathUnderFedoraRoot, version);
                // We can still cache this specific version, though, at its version key
                logger.LogInformation("Setting versioned cache entry for {pathUnderFedoraRoot}, version {version}",
                    request.PathUnderFedoraRoot, version);
                memoryCache.Set(versionedCacheKey, ag, expiry);
                // But we only set the path-only cached token if we just retrieved the LATEST, HEAD version
                if (ag.StorageMap?.HeadVersion.OcflVersion == version)
                {
                    logger.LogInformation("Version {version} is the HEAD for {pathUnderFedoraRoot}, so will cache the path-only token",
                        version, request.PathUnderFedoraRoot);
                    var pathCacheKey = CacheKey(request.PathUnderFedoraRoot, null);
                    memoryCache.Set(pathCacheKey, version, expiry);
                }
                else
                {
                    logger.LogInformation("The HEAD version for {pathUnderFedoraRoot} is {headVersion}, so will NOT CACHE the path-only token",
                        request.PathUnderFedoraRoot, ag.StorageMap?.HeadVersion.OcflVersion);
                }
            }
            else
            {
                // This should never happen?
                logger.LogWarning(
                    "The resource at {PathUnderFedoraRoot} is an Archival Group but does not have a version",
                    request.PathUnderFedoraRoot);
            }
        }
        return result;
    }

    private string CacheKey(string? path, string? version)
    {
        // This must avoid any possible (though very unlikely) collisions with other actual paths
        if (version != null)
        {
            return $"AG_///_{path ?? ""}_///_{version}";
        }

        return $"AG_///_{path ?? ""}";
    }
}