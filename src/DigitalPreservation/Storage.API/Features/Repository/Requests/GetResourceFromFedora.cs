using System.Collections.Concurrent;
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
    /// <summary>
    /// One in-flight Fedora fetch per path, shared by every concurrent request for that path
    /// (issue #387, split from #244). Without this, a slow build of a large Archival Group - #244
    /// measured over 600s and ~27 points of the task's memory for one with 2,796 files - runs again,
    /// in full, for every caller that arrives (or retries after timing out) while the first build is
    /// still running; that is what makes the cache-miss path dangerous rather than merely slow.
    /// Keyed on the path alone: the handler doesn't know whether a path is an Archival Group until
    /// the fetch returns, and a caller joining a fetch of something else is harmless - it is just
    /// "wait for this path's in-flight result". Per process: a second Storage API task still builds
    /// its own copy, which is fine for now (#244's option 4 is about sharing across instances).
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<Result<PreservedResource?>>>> InFlightFetches = new();

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
            logger.LogInformation("There is a cached token for {PathUnderFedoraRoot}: {Version}",
                request.PathUnderFedoraRoot, possibleAgVersion);
            var possibleAg = memoryCache.Get<ArchivalGroup>(CacheKey(request.PathUnderFedoraRoot, possibleAgVersion));
            if (possibleAg != null)
            {
                logger.LogInformation("Retrieved cached Archival Group {PathUnderFedoraRoot} for cached version string {Version}",
                    request.PathUnderFedoraRoot, possibleAgVersion);
                // It is HIGHLY LIKELY that this is the current version. But we can't guarantee it.
                // But we know it must be an archival group (we wouldn't have cached it otherwise).
                logger.LogInformation("Checking if this is indeed the HEAD version:");
                var versionResult = await fedoraClient.GetArchivalGroupVersion(request.PathUnderFedoraRoot);
                if (versionResult is { Success: true, Value: not null } &&
                    versionResult.Value.OcflVersion == possibleAgVersion)
                {
                    logger.LogInformation("HEAD version of {PathUnderFedoraRoot} is {Version}",
                        request.PathUnderFedoraRoot, versionResult.Value.OcflVersion);
                    return Result.Ok<PreservedResource?>(possibleAg);
                }
                logger.LogWarning("Latest cached HEAD version value is {CachedVersion} but actual version is {ActualVersion}",
                    possibleAgVersion, versionResult.Value?.OcflVersion);
            }
            logger.LogWarning("No Archival Group in cache for cached version string {PathUnderFedoraRoot} at path {Version}",
                request.PathUnderFedoraRoot, possibleAgVersion);
        }

        return await FetchCoalesced(request, cancellationToken);
    }

    /// <summary>
    /// Joins an in-flight fetch of <paramref name="request"/>'s path if there is one, otherwise
    /// starts one and becomes the leader other concurrent callers will join. Leadership is decided
    /// by reference equality against what <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,TValue)"/>
    /// actually stored, not by a flag set inside a factory callback: <c>GetOrAdd</c>'s factory
    /// overload can run on more than one thread for a genuinely new key, with only one result kept,
    /// so a captured "I created this" flag would lie for whichever thread's object was discarded.
    /// Constructing the <see cref="Lazy{T}"/> itself is cheap - the fetch only starts once something
    /// reads <c>.Value</c> - so losing the race here costs nothing.
    /// </summary>
    private async Task<Result<PreservedResource?>> FetchCoalesced(GetResourceFromFedora request, CancellationToken cancellationToken)
    {
        var path = request.PathUnderFedoraRoot ?? string.Empty;

        // The removal is the last thing this same task does, before it completes - not a detached
        // continuation - so every awaiter of myFetch.Value only ever observes it after the entry is
        // already gone. A continuation scheduled separately (TaskScheduler.Default) would race a
        // joiner that reacts to the result the instant it arrives: it could still find the
        // now-stale entry here and rejoin the very fetch it just proved was stale, instead of
        // starting a fresh one (issue #387's "fetched again" requirement).
        Lazy<Task<Result<PreservedResource?>>> myFetch = null!;
        myFetch = new Lazy<Task<Result<PreservedResource?>>>(async () =>
        {
            try
            {
                // The fetch itself is not tied to any one caller's cancellationToken - the first
                // caller giving up must not cancel a build other callers are still waiting on. Each
                // caller still stops waiting on its own token below, via WaitAsync (issue #387).
                return await FetchAndCache(request);
            }
            finally
            {
                // The KeyValuePair overload only removes this exact entry, so a path that already
                // moved on to a newer fetch by the time this runs is left alone.
                InFlightFetches.TryRemove(
                    new KeyValuePair<string, Lazy<Task<Result<PreservedResource?>>>>(path, myFetch));
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        var sharedFetch = InFlightFetches.GetOrAdd(path, myFetch);
        var isLeader = ReferenceEquals(myFetch, sharedFetch);
        logger.LogInformation(
            isLeader
                ? "Starting Fedora fetch for {PathUnderFedoraRoot}"
                : "Joining in-flight Fedora fetch for {PathUnderFedoraRoot}",
            request.PathUnderFedoraRoot);

        var result = await sharedFetch.Value.WaitAsync(cancellationToken);

        if (!isLeader && result is { Success: true, Value: ArchivalGroup joinedAg })
        {
            // A fetch that started before an import committed would otherwise hand a joiner the
            // previous version as "the latest" (issue #387) - the leader doesn't need this check,
            // since it only just started its own fresh fetch, same as the cache-miss path always
            // has. Same cheap check the cache-hit path above already does; anything other than a
            // confirmed match is treated as stale, same as that path's own fall-through below it.
            var versionResult = await fedoraClient.GetArchivalGroupVersion(path);
            var isHead = versionResult is { Success: true, Value: not null } &&
                         versionResult.Value.OcflVersion == joinedAg.Version?.OcflVersion;
            if (!isHead)
            {
                logger.LogInformation(
                    "Joined fetch for {PathUnderFedoraRoot} returned version {JoinedVersion}, but HEAD is now {HeadVersion}; fetching again",
                    request.PathUnderFedoraRoot, joinedAg.Version?.OcflVersion, versionResult.Value?.OcflVersion);
                return await FetchCoalesced(request, cancellationToken);
            }
        }

        return result;
    }

    private async Task<Result<PreservedResource?>> FetchAndCache(GetResourceFromFedora request)
    {
        // OK, so now we have to get the resource
        var result = await fedoraClient.GetResource(request.PathUnderFedoraRoot, cancellationToken: CancellationToken.None);

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
                logger.LogInformation("Setting versioned cache entry for {PathUnderFedoraRoot}, version {Version}",
                    request.PathUnderFedoraRoot, version);
                memoryCache.Set(versionedCacheKey, ag, expiry);
                // But we only set the path-only cached token if we just retrieved the LATEST, HEAD version
                if (ag.StorageMap?.HeadVersion.OcflVersion == version)
                {
                    logger.LogInformation("Version {Version} is the HEAD for {PathUnderFedoraRoot}, so will cache the path-only token",
                        version, request.PathUnderFedoraRoot);
                    var pathCacheKey = CacheKey(request.PathUnderFedoraRoot, null);
                    memoryCache.Set(pathCacheKey, version, expiry);
                }
                else
                {
                    logger.LogInformation("The HEAD version for {PathUnderFedoraRoot} is {HeadVersion}, so will NOT CACHE the path-only token",
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

    private static string CacheKey(string? path, string? version)
    {
        // This must avoid any possible (though very unlikely) collisions with other actual paths
        if (version != null)
        {
            return $"AG_///_{path ?? ""}_///_{version}";
        }

        return $"AG_///_{path ?? ""}";
    }
}
