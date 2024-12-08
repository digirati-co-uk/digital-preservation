using System.Net;
using System.Security.AccessControl;
using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Storage.API.Fedora.Http;
using Storage.API.Fedora.Model;
using Storage.API.Fedora.Vocab;
using Storage.Repository.Common;

namespace Storage.API.Fedora;

internal class FedoraClient(
    HttpClient httpClient,
    IStorage storage,
    ILogger<FedoraClient> logger,
    IOptions<FedoraOptions> fedoraOptions,
    Converters converters,
    IMemoryCache cache,
    IStorageMapper storageMapper,
    FedoraDB fedoraDB) : IFedoraClient
{
    public readonly bool RequireChecksum = fedoraOptions.Value.RequireDigestOnBinary;
    public readonly string FedoraBucket = fedoraOptions.Value.Bucket;
    
    public async Task<Result<PreservedResource?>> GetResource(string? pathUnderFedoraRoot, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var uri = converters.GetFedoraUri(pathUnderFedoraRoot);
        var typeRes = await GetResourceType(pathUnderFedoraRoot, transaction);
        if (!typeRes.Success)
        {
            return Result.ConvertFail<string?, PreservedResource?>(typeRes);
        }
        
        if (typeRes.Value == nameof(ArchivalGroup))
        {
            var agResult = await GetPopulatedArchivalGroup(pathUnderFedoraRoot!, null, transaction);
            if (agResult.Success)
            {
                return Result.Ok(agResult.Value as PreservedResource);
            }
            return Result.ConvertFail<ArchivalGroup?, PreservedResource>(agResult);
        }
        
        var storageMap = await FindParentStorageMap(uri);
        PreservedResource? resource = null;
        if (typeRes.Value == nameof(Binary))
        {
            var binary = await GetResourceInternal<Binary>(uri, transaction);
            if(storageMap != null && binary != null)
            {
                PopulateOrigin(storageMap, binary);
            }
            resource = binary;
        }
        
        else if (typeRes.Value == nameof(Container))
        {
            // this is also true for archival group so test this last
            var container = await GetPopulatedContainer(
                uri, 
                isArchivalGroup: false, 
                recurse: false, 
                canUseDb: storageMap == null,
                transaction);
            if (storageMap != null && container != null)
            {
                PopulateOrigins(storageMap, container);
            }
            resource = container;
        }
        if (storageMap != null && resource != null)
        {
            resource.PartOf = converters.ConvertToRepositoryUri(storageMap.ArchivalGroup);
        }
        return Result.Ok(resource);
    }

    public async Task<Result<ArchivalGroup?>> GetPopulatedArchivalGroup(string pathUnderFedoraRoot, string? version = null, Transaction? transaction = null)
    {
        var uri = converters.GetFedoraUri(pathUnderFedoraRoot);
        return await GetPopulatedArchivalGroup(uri, version, transaction);
    }

    private async Task<Result<ArchivalGroup?>> GetPopulatedArchivalGroup(Uri uri, string? version = null, Transaction? transaction = null)
    {
        var versions = await GetFedoraVersions(uri);
        if (versions == null)
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"No versions found for {uri}");
        }
        var storageMap = await GetCacheableStorageMap(uri, version, true);
        MergeVersions(versions, storageMap.AllVersions);

        if(await GetPopulatedContainer(uri, true, true, false, transaction) is not ArchivalGroup archivalGroup)
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"{uri} is not an Archival Group");
        }
        if(archivalGroup.Id != converters.ConvertToRepositoryUri(uri))
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"{uri} does not match {archivalGroup.Id}");
        }

        archivalGroup.Origin = storageMapper.GetArchivalGroupOrigin(archivalGroup.Id)!.S3UriInBucket(FedoraBucket);
        archivalGroup.Versions = versions;
        archivalGroup.StorageMap = storageMap;
        archivalGroup.Version = versions.Single(v => v.OcflVersion == storageMap.Version.OcflVersion);
        if(archivalGroup.Version.Equals(archivalGroup.StorageMap.HeadVersion))
        {
            PopulateOrigins(storageMap, archivalGroup);
        }
        return Result.Ok(archivalGroup);
    }
    
    private async Task<T?> GetResourceInternal<T>(Uri uri, Transaction? transaction = null) where T : Resource
    {
        var isBinary = typeof(T) == typeof(Binary);
        var reqUri = isBinary ? uri.MetadataUri() : uri;
        var request = MakeHttpRequestMessage(reqUri, HttpMethod.Get)
            .InTransaction(transaction)
            .ForJsonLd(); 
        var response = await httpClient.SendAsync(request);

        if (isBinary)
        {
            var fileResponse = await MakeFedoraResponse<BinaryMetadataResponse>(response);
            return converters.MakeBinary(fileResponse!) as T;
        }

        var directoryResponse = await MakeFedoraResponse<FedoraJsonLdResponse>(response);
        if (directoryResponse == null) return null;
            
        if (response.HasArchivalGroupTypeHeader())
        {
            return converters.MakeArchivalGroup(directoryResponse) as T;
        }
        return converters.MakeContainer(directoryResponse) as T;
    }
    
    private async Task<Result<string?>> GetResourceType(Uri uri, Transaction? transaction = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Head, uri)
            .InTransaction(transaction);
        req.Headers.Accept.Clear();
        var headResponse = await httpClient.SendAsync(req);
        if (headResponse.IsSuccessStatusCode)
        {
            string? typeName;
            if (headResponse.HasArchivalGroupTypeHeader())
            {
                typeName = nameof(ArchivalGroup);
            }
            else if (headResponse.HasBinaryTypeHeader())
            {
                typeName = nameof(Binary);
            }
            else if (headResponse.HasBasicContainerTypeHeader())
            {
                typeName = nameof(Container);
            }
            else
            {
                return Result.Fail<string?>(ErrorCodes.UnknownError, headResponse.ReasonPhrase ?? "Unknown Type");
            }
            if (typeName.HasText())
            {
                return Result.Ok(typeName);
            }
        }

        if (headResponse.IsTombstone())
        {
            return Result.Ok(RepositoryTypes.Tombstone);
        }

        return headResponse.StatusCode switch
        {
            HttpStatusCode.NotFound => Result.Fail<string?>(ErrorCodes.NotFound, "Not found"),
            HttpStatusCode.Unauthorized => Result.Fail<string?>(ErrorCodes.Unauthorized, "Unauthorized"),
            _ => Result.Fail<string?>(ErrorCodes.UnknownError, "Status from repository was " + headResponse.StatusCode)
        };
    }
    
    public async Task<Result<string?>> GetResourceType(string? pathUnderFedoraRoot, Transaction? transaction = null)
    {
        var uri = converters.GetFedoraUri(pathUnderFedoraRoot);
        return await GetResourceType(uri, transaction);
    }

    public async Task<Result<ArchivalGroup?>> GetValidatedArchivalGroupForImportJob(string pathUnderFedoraRoot, Transaction? transaction = null)
    {
        var info = await GetResourceType(pathUnderFedoraRoot, transaction);
        if (info is { Success: true, Value: nameof(ArchivalGroup) })
        {
            var ag = await GetPopulatedArchivalGroup(pathUnderFedoraRoot, null, transaction);
            return ag;
        }
        if (info.ErrorCode == ErrorCodes.NotFound)
        {
            var validateResult = await ContainerCanBeCreatedAtPath(pathUnderFedoraRoot, false, transaction);
            if (validateResult.Failure)
            {
                return Result.Cast<Container?, ArchivalGroup?>(validateResult);
            }
            return Result.Ok<ArchivalGroup>(null);
        }
        return Result.Fail<ArchivalGroup?>(info.ErrorCode ?? ErrorCodes.UnknownError,
            $"Cannot create Archival Group {pathUnderFedoraRoot} - {info.ErrorMessage}");
    }

    /// <summary>
    /// Walk up the Uri of a resource until we get to an ArchivalGroup or to the Fedora root
    /// </summary>
    /// <param name="resourceUri"></param>
    /// <returns></returns>
    private async Task<StorageMap?> FindParentStorageMap(Uri resourceUri)
    {
        if(converters.IsFedoraRoot(resourceUri))
        {
            return null;
        }

        var testUris = new List<Uri>();
        
        var parentUri = resourceUri.GetParentUri(trimTrailingSlash: true);
        while (parentUri != null && !converters.IsFedoraRoot(parentUri))
        {
            // We know the root is not an Archival Group, we can skip testing it.
            testUris.Add(parentUri);
            parentUri = parentUri.GetParentUri(trimTrailingSlash: true);
        }
        
        if(testUris.Count == 0)
        {
            return null;
        }
        
        StorageMap? storageMap;
        // first test the cache
        foreach (var testUri in testUris)
        {
            if (cache.TryGetValue(GetStorageMapCacheKey(testUri, null), out storageMap))
            {
                return storageMap;
            }
        }
        // now we need to actually probe for a storage map
        foreach (var testUri in testUris)
        {
            var testUriResult = await GetResourceType(testUri);
            if (testUriResult.Failure)
            {
                // This is a genuine THROW scenario, it's bad
                throw new InvalidOperationException("Walk up known tree could not resolve resource");
            }
            if (testUriResult.Value == nameof(ArchivalGroup))
            {
                storageMap = await GetCacheableStorageMap(testUri);
                return storageMap;
            }
        }
        return null;
    }

    private async Task<StorageMap> GetCacheableStorageMap(Uri archivalGroupUri, string? version = null, bool refresh = false)
    {
        string cacheKey = GetStorageMapCacheKey(archivalGroupUri, version);
        if (refresh || !cache.TryGetValue(cacheKey, out StorageMap? storageMap))
        {
            storageMap = await storageMapper.GetStorageMap(archivalGroupUri, version);
            var cacheOpts = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(10));
            cache.Set(cacheKey, storageMap, cacheOpts);
        }
        return storageMap!;
    }
    
    private static string GetStorageMapCacheKey(Uri archivalGroupUri, string? version)
    {
        return $"{archivalGroupUri}?version={version}";
    }
    
    private async Task<Result<Container?>> ContainerCanBeCreatedAtPath(string pathUnderFedoraRoot, bool isWithinArchivalGroup, Transaction? transaction = null)
    {
        // Does the slug only contain valid chars? ✓
        var slug = pathUnderFedoraRoot.GetSlug();
        if (!PreservedResource.ValidSlug(slug))
        {
            return Result.Fail<Container?>(ErrorCodes.BadRequest, "Invalid slug: " + slug);
        }
        
        // Is there already something at this path? - no ✓
        var existing = await GetResourceType(pathUnderFedoraRoot, transaction);
        if (existing.ErrorCode != ErrorCodes.NotFound)
        {
            // This is the ONLY acceptable state
            return Result.Fail<Container?>(ErrorCodes.Conflict,
                $"Resource already exists at {pathUnderFedoraRoot} ({existing.Value})");
        }
        
        // Is there a Container at the parent of this path - yes ✓
        // Is the parent Container an Archival Group or part of an Archival Group? no ✓ 
        string? parentPath = pathUnderFedoraRoot.GetParent();
        if (isWithinArchivalGroup)
        {
            var parent = await GetResourceType(parentPath, transaction);
            if (parent is { Success: true, Value: nameof(ArchivalGroup) } || parent.Value == nameof(Container))
            {
                // We only need to check the immediate parent if we are creating containers within an archival group
                return Result.Ok<Container?>(null);
            }
            return Result.Fail<Container?>(ErrorCodes.Conflict,
                $"Parent of {pathUnderFedoraRoot} must be a Container or Archival Group, {parentPath} is a {parent.Value}.");
        }
        while (parentPath != null)
        {
            var parent = await GetResourceType(parentPath, transaction);
            if (parent is not { Success: true, Value: nameof(Container) }) // i.e., not ArchivalGroup or Binary or whatever
            {
                var typeMessage = parent.Value.HasText() ? "is a " + parent.Value : "does not exist.";
                return Result.Fail<Container?>(ErrorCodes.Conflict,
                    $"Ancestors of '{pathUnderFedoraRoot}' must all be Containers, '{parentPath}' {typeMessage}.");
            }
            parentPath = parentPath.GetParent();
        }
        return Result.Ok<Container?>(null);
    }

    public async Task<Result<Container?>> CreateContainer(string pathUnderFedoraRoot, string? name,
        Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        return await CreateContainerWithChecks(pathUnderFedoraRoot, name, false, transaction, cancellationToken);
    }

    public async Task<Result<Container?>> CreateContainerWithinArchivalGroup(string pathUnderFedoraRoot, string? name, Transaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateContainerWithChecks(pathUnderFedoraRoot, name, true, transaction, cancellationToken);
    }
    
    private async Task<Result<Container?>> CreateContainerWithChecks(string pathUnderFedoraRoot, string? name, bool isWithinArchivalGroup, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var validateResult = await ContainerCanBeCreatedAtPath(pathUnderFedoraRoot, isWithinArchivalGroup, transaction);
        if (validateResult.Failure)
        {
            return validateResult;
        }
        // All tests passed
        var container = await CreateContainerInternal(false, pathUnderFedoraRoot, name, transaction);
        return Result.Ok(container);
    }


    private async Task<Container?> CreateContainerInternal(
        bool asArchivalGroup,
        string pathUnderFedoraRoot,
        string? name,
        Transaction? transaction = null)
    {
        // TODO: This follows the prototype and performs a POST, adding a _desired_ slug.
        // Investigate whether a PUT would be better (or worse)
        var fedoraUri = converters.GetFedoraUri(pathUnderFedoraRoot);
        
        var req = MakeHttpRequestMessage(fedoraUri.GetParentUri()!, HttpMethod.Post)
            .InTransaction(transaction)
            .WithName(name)
            .WithSlug(fedoraUri.GetSlug()!);
        if (asArchivalGroup)
        {
            req.AsArchivalGroup();
        }
        var response = await httpClient.SendAsync(req);
        response.EnsureSuccessStatusCode();

        if(asArchivalGroup)
        {
            // TODO - could combine this with .WithName(name) and make a more general .WithRdf(string name = null, string type = null)
            // so that the extra info goes on the initial POST and this little PATCH isn't required
            var patchReq = MakeHttpRequestMessage(response.Headers.Location!, HttpMethod.Patch)
                .InTransaction(transaction);
            // We give the AG this _additional_ type so that we can see that it's an AG when we get bulk child members back from .WithContainedDescriptions
            patchReq.AsInsertTypePatch("<http://purl.org/dc/dcmitype/Collection>");
            var patchResponse = await httpClient.SendAsync(patchReq);
            patchResponse.EnsureSuccessStatusCode();
        }
        // The body is the new resource URL
        var newReq = MakeHttpRequestMessage(response.Headers.Location!, HttpMethod.Get)
            .InTransaction(transaction)
            .ForJsonLd();
        var newResponse = await httpClient.SendAsync(newReq);

        var containerResponse = await MakeFedoraResponse<FedoraJsonLdResponse>(newResponse);
        if (containerResponse == null)
        {
            return null;
        }
        return asArchivalGroup ? converters.MakeArchivalGroup(containerResponse) : converters.MakeContainer(containerResponse);
    }

    public async Task<Result<Binary?>> PutBinary(Binary binary, Transaction transaction, CancellationToken cancellationToken = default)
    {        
        // TODO: Can we PUT the resource and set it's dc:title in the same PUT request?
        // At the moment we have to make a separate update to the metadata endpoint.
        // verify that parent is a container first?
        await EnsureChecksum(binary, false);
        var req = await MakeBinaryPut(binary, transaction);
        var response = await httpClient.SendAsync(req, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            // https://github.com/fcrepo/fcrepo/pull/2044
            // see also https://github.com/whikloj/fcrepo4-tests/blob/fcrepo-6/archival_group_tests.py#L149-L190
            // 410 indicates that this URI has a tombstone sitting at it; it has previously been DELETEd.
            // But we want to reinstate a binary.

            // Log or record somehow that this has happened?
            var retryReq = (await MakeBinaryPut(binary, transaction))
                .OverwriteTombstone();
            response = await httpClient.SendAsync(retryReq, cancellationToken);
        }
        response.EnsureSuccessStatusCode();
        var metadataUri = req.RequestUri!.MetadataUri();
        
        var newReq = MakeHttpRequestMessage(metadataUri, HttpMethod.Get)
            .InTransaction(transaction)
            .ForJsonLd();
        var newResponse = await httpClient.SendAsync(newReq, cancellationToken);

        var binaryResponse = await MakeFedoraResponse<BinaryMetadataResponse>(newResponse);
        if (binaryResponse!.Title == null)
        {
            // The binary resource does not have a dc:title property yet
            var patchReq = MakeHttpRequestMessage(metadataUri, HttpMethod.Patch)
                .InTransaction(transaction);
            patchReq.AsInsertTitlePatch(binary.Name!);
            var patchResponse = await httpClient.SendAsync(patchReq, cancellationToken);
            patchResponse.EnsureSuccessStatusCode();
            // now ask again:
            var retryMetadataReq = MakeHttpRequestMessage(metadataUri, HttpMethod.Get)
               .InTransaction(transaction)
               .ForJsonLd();
            var afterPatchResponse = await httpClient.SendAsync(retryMetadataReq, cancellationToken);
            binaryResponse = await MakeFedoraResponse<BinaryMetadataResponse>(afterPatchResponse);
        }
        var madeBinary = converters.MakeBinary(binaryResponse!);
        if (madeBinary.Digest != binary.Digest)
        {
            return Result.Fail<Binary?>(ErrorCodes.UnknownError, "Fedora-generated checksum doesn't match ours.");
        }
        return Result.Ok(madeBinary);
    }
    
    
    private async Task<Result> EnsureChecksum(Binary binary, bool validate)
    {
        bool isMissing = string.IsNullOrWhiteSpace(binary.Digest);
        if (isMissing || validate)
        {
            if (isMissing && RequireChecksum)
            {
                return Result.Fail($"Missing digest on incoming Binary {binary.Id}");
            }

            var expectedDigestResult = await storage.GetExpectedDigest(binary.Origin, binary.Digest);
            if (expectedDigestResult.Success)
            {
                binary.Digest = expectedDigestResult.Value;
                return Result.Ok();
            }
            return Result.Fail(expectedDigestResult.ErrorCode!, expectedDigestResult.ErrorMessage);
        }
        return Result.Ok();
    }

    private async Task<HttpRequestMessage> MakeBinaryPut(Binary binary, Transaction transaction)
    {
        var fedoraLocation = converters.GetFedoraUri(binary.Id.GetPathUnderRoot());
        var req = MakeHttpRequestMessage(fedoraLocation, HttpMethod.Put)
            .InTransaction(transaction)
            .WithDigest(binary.Digest, "sha-256"); // move algorithm choice to constant

        // TODO: Need something better than this for large files.
        // How would we transfer a 10GB file for example?
        // Also this is grossly inefficient, we've already read the stream to look at the checksum.
        // We should keep the byte array... but then what if it's huge?

        // This should instead reference the file in S3, for Fedora to fetch
        // https://fedora-project.slack.com/archives/C8B5TSR4J/p1710164226000799
        // ^ not possible rn - but can use a signed HTTP url to fetch! (TODO)
        var byteArray = await storage.GetBytes(binary.Origin!);
        req.Content = new ByteArrayContent(byteArray)
            .WithContentType(binary.ContentType);
        
        // Still set the content disposition to give the file within Fedora an ebucore:filename triple:
        req.Content.WithContentDisposition(binary.Name);
        return req;
    }

    /// <summary>
    /// For deleting resources within AGs
    /// It still works outside AGs but will leave tombstones behind (correctly)
    ///
    /// The DeleteContainerOutsideOfArchivalGroup is used for additionally specifying a purge
    /// </summary>
    /// <param name="resource"></param>
    /// <param name="transaction"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<Result<PreservedResource>> Delete(PreservedResource resource, Transaction transaction, CancellationToken cancellationToken = default)
    {
        try
        {
            var fedoraLocation = converters.GetFedoraUri(resource.Id.GetPathUnderRoot());
            var req = MakeHttpRequestMessage(fedoraLocation, HttpMethod.Delete)
                .InTransaction(transaction);

            var response = await httpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Result.OkNotNull(resource);
            }
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => Result.FailNotNull<PreservedResource>(ErrorCodes.NotFound, "Not found"),
                HttpStatusCode.Unauthorized => Result.FailNotNull<PreservedResource>(ErrorCodes.Unauthorized, "Unauthorized"),
                _ => Result.FailNotNull<PreservedResource>(ErrorCodes.UnknownError, "Status from repository was " + response.StatusCode)
            };
        }
        catch (Exception e)
        {
            return Result.FailNotNull<PreservedResource>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> DeleteContainerOutsideOfArchivalGroup(string pathUnderFedoraRoot, bool purge,
        CancellationToken cancellationToken)
    {
        // GetResource can return a tombstone... - check that
        // Now get the resource and make sure it's an EMPTY container OUTSIDE of an AG
         
        try
        {
            var fedoraLocation = converters.GetFedoraUri(pathUnderFedoraRoot);
            var response = await httpClient.DeleteAsync(fedoraLocation, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (purge)
                {
                    var typeAtLocation = await GetResourceType(fedoraLocation);
                    if (typeAtLocation.Value == RepositoryTypes.Tombstone)
                    {
                        var delTombstoneResponse =
                            await httpClient.DeleteAsync(fedoraLocation.TombstoneUri(), cancellationToken);
                        if (delTombstoneResponse.IsSuccessStatusCode)
                        {
                            return Result.Ok();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ErrorCodes.UnknownError, ex.Message);
        }

        return Result.Fail(ErrorCodes.UnknownError);
    }

    public async Task<Result<ArchivalGroup?>> CreateArchivalGroup(string pathUnderFedoraRoot, string name, Transaction transaction, CancellationToken cancellationToken = default)
    {
        var ag = await CreateContainerInternal(true, pathUnderFedoraRoot, name, transaction) as ArchivalGroup;
        return Result.Ok(ag);
    }

    private async Task<Container?> GetPopulatedContainer(Uri uri, bool isArchivalGroup, bool recurse, bool canUseDb, Transaction? transaction = null)
    {
        // temporarily use recurse as a flag to use DB.
        Container? dbContainer = null;
        if (canUseDb && fedoraDB.Available)
        {
            dbContainer = await fedoraDB.GetPopulatedContainer(uri);
            if (dbContainer is { Containers.Count: < 20, Binaries.Count: < 20 })
            {
                // Has few enough child items that WithContainedDescriptions is OK to use
                dbContainer = null;
            }
        }
        // TODO: This is not using transaction - should it?
        var request = MakeHttpRequestMessage(uri, HttpMethod.Get).ForJsonLd();
        if (dbContainer == null)
        {
            request.WithContainedDescriptions();
        }
        var response = await httpClient.SendAsync(request);
        bool hasArchivalGroupHeader = response.HasArchivalGroupTypeHeader();
        if (isArchivalGroup && !hasArchivalGroupHeader)
        {
            throw new InvalidOperationException("Response is not an Archival Group, when Archival Group expected");
        }
        if (!isArchivalGroup && hasArchivalGroupHeader)
        {
            throw new InvalidOperationException("Response is an Archival Group, when Basic Container expected");
        }
        
        
        // WithContainedDescriptions could return @graph, or it could return a single object if the container has no children
        // This is the only place that needs to check for @graph so can remain here
        var stream = await response.Content.ReadAsStreamAsync();
        
        using var jDoc = await JsonDocument.ParseAsync(stream); // jDoc is a Fedora JSON response
        
        JsonElement[]? containerAndContained = null;
        if (jDoc.RootElement.TryGetProperty("@graph", out JsonElement graph))
        {
            containerAndContained = [.. graph.EnumerateArray()];
        }
        else
        {
            if (jDoc.RootElement.TryGetProperty("@id", out _))
            {
                containerAndContained = [jDoc.RootElement];
            }
        }
        if (containerAndContained == null || containerAndContained.Length == 0) 
        {
            throw new InvalidOperationException("Could not parse Container response");
        }
        if (containerAndContained[0].GetProperty("@id").GetString() != uri.ToString())
        {
            throw new InvalidOperationException("First resource in @graph should be the asked-for URI");
        }
            
        var fedoraObject = containerAndContained[0].Deserialize<FedoraJsonLdResponse>();
        var topContainer = isArchivalGroup ? converters.MakeArchivalGroup(fedoraObject!) : converters.MakeContainer(fedoraObject!);
        if (dbContainer != null)
        {
            if (isArchivalGroup)
            {
                // This should never happen, just a guard against bugs above
                throw new InvalidOperationException("Should not create AG from database!");
            }

            topContainer.Containers = dbContainer.Containers;
            topContainer.Binaries = dbContainer.Binaries;
            return topContainer;
        }
        
        // Make a map of the IDs
        Dictionary<string, JsonElement> dict = containerAndContained.ToDictionary(x => x.GetProperty("@id").GetString()!);

        // Get the contains property which may be a single value or an array
        var idsFromContainmentPredicate = GetIdsFromContainsProperty(containerAndContained[0]);
        idsFromContainmentPredicate.Sort();
        foreach (var id in idsFromContainmentPredicate)
        {
            var resource = dict[id];
            if (resource.HasType("fedora:Container"))
            {
                var fedoraContainer = resource.Deserialize<FedoraJsonLdResponse>()!;
                Container? container;
                if (recurse)
                {
                    container = await GetPopulatedContainer(fedoraContainer.Id, false, true, canUseDb, transaction);
                }
                else
                {
                    container = converters.MakeContainer(fedoraContainer);
                }
                topContainer.Containers.Add(container!);
            }
            else if (resource.HasType("fedora:Binary"))
            {
                var fedoraBinary = resource.Deserialize<BinaryMetadataResponse>();
                var binary = converters.MakeBinary(fedoraBinary!);
                topContainer.Binaries.Add(binary);
            }
        }
        return topContainer;
    }


    private static HttpRequestMessage MakeHttpRequestMessage(Uri uri, HttpMethod method)
    {
        var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Headers.Accept.Clear();
        return requestMessage;
    }   
    
    private HttpRequestMessage MakeHttpRequestMessage(string path, HttpMethod method)
    {
        var uri = new Uri(path, UriKind.Relative);
        return MakeHttpRequestMessage(uri, method);
    }
    
    private List<string> GetIdsFromContainsProperty(JsonElement element)
    {
        List<string> childIds = [];
        if (element.TryGetProperty("contains", out JsonElement contains))
        {
            if (contains.ValueKind == JsonValueKind.String)
            {
                childIds = [contains.GetString()!];
            }
            else if (contains.ValueKind == JsonValueKind.Array)
            {
                childIds = contains.EnumerateArray().Select(x => x.GetString()!).ToList();
            }
        }
        return childIds;
    }


    public async Task<ConnectivityCheckResult> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await httpClient.GetAsync("./fcr:systeminfo", cancellationToken);
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd,
                Success = true
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Fedora is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd,
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    
    private static async Task<T?> MakeFedoraResponse<T>(HttpResponseMessage response) where T : FedoraJsonLdResponse
    {
        // works for SINGLE resources, not contained responses that send back a @graph
        var fedoraResponse = await response.Content.ReadFromJsonAsync<T>();
        if (fedoraResponse != null)
        {
            fedoraResponse.HttpResponseHeaders = response.Headers;
            fedoraResponse.HttpStatusCode = response.StatusCode;
            fedoraResponse.Body = await response.Content.ReadAsStringAsync();
        }
        return fedoraResponse;
    }
    
    private void PopulateOrigins(StorageMap storageMap, Container container)
    {
        foreach(var binary in container.Binaries)
        {
            PopulateOrigin(storageMap, binary);
        }
        foreach (var childContainer in container.Containers)
        {
            PopulateOrigins(storageMap, childContainer);
        }
    }
    
    private static void PopulateOrigin(StorageMap storageMap, Binary binary)
    {
        if (storageMap.StorageType == StorageTypes.S3)
        {
            binary.Origin = new Uri($"s3://{storageMap.Root}/{storageMap.ObjectPath}/{storageMap.Hashes[binary.Digest!]}");
        }
        // filesystem later
    }
    
    private async Task<ObjectVersion[]?> GetFedoraVersions(Uri uri)
    {
        var request = MakeHttpRequestMessage(uri.VersionsUri(), HttpMethod.Get)
            .ForJsonLd();

        var response = await httpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        
        var content = await response.Content.ReadAsStringAsync();

        using var jDoc = JsonDocument.Parse(content);
        var childIds = GetIdsFromContainsProperty(jDoc.RootElement);
        // We could go and request each of these.
        // But... the Fedora API gives the created and lastmodified date of the original, not the version, when you ask for a versioned response.
        // Is that a bug?
        // We're not going to learn anything more than we would by parsing the memento path elements - which is TERRIBLY non-REST-y
        return childIds
            .Select(id => id.Split('/').Last())
            .Select(p => new ObjectVersion { MementoTimestamp = p, MementoDateTime = p.DateTimeFromMementoTimestamp() })
            .OrderBy(ov => ov.MementoTimestamp)
            .ToArray();
    }
    
    private void MergeVersions(ObjectVersion[] fedoraVersions, ObjectVersion[] ocflVersions)
    {
        if(fedoraVersions.Length != ocflVersions.Length)
        {
            throw new InvalidOperationException("Fedora reports a different number of versions from OCFL");
        }
        for(int i = 0; i < fedoraVersions.Length; i++)
        {
            if(fedoraVersions[i].MementoTimestamp != ocflVersions[i].MementoTimestamp)
            {
                throw new InvalidOperationException($"Fedora reports a different MementoTimestamp {fedoraVersions[i].MementoTimestamp} from OCFL: {ocflVersions[i].MementoTimestamp}");
            }
            fedoraVersions[i].OcflVersion = ocflVersions[i].OcflVersion;
        }
    }


    public async Task<Transaction> BeginTransaction()
    {
        var req = MakeHttpRequestMessage("./fcr:tx", HttpMethod.Post); // note URI construction because of the colon
        var response = await httpClient.SendAsync(req);
        response.EnsureSuccessStatusCode();
        var tx = new Transaction
        {
            Location = response.Headers.Location!
        };
        if (response.Headers.TryGetValues("Atomic-Expires", out IEnumerable<string>? values))
        {
            // This header is not being returned in the version we're using
            tx.Expires = DateTime.Parse(values.First());
        } 
        else
        {
            // ... so we'll need to obtain it like this, I think
            await KeepTransactionAlive(tx);
        }
        return tx;
    }

    public async Task CheckTransaction(Transaction tx)
    {
        HttpRequestMessage req = MakeHttpRequestMessage(tx.Location, HttpMethod.Get);
        var response = await httpClient.SendAsync(req);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                tx.Expired = false;
                break;
            case HttpStatusCode.NotFound:
                // error?
                break;
            case HttpStatusCode.Gone:
                tx.Expired = true;
                break;
            default:
                // error?
                break;
        }
    }

    public async Task KeepTransactionAlive(Transaction tx)
    {
        HttpRequestMessage req = MakeHttpRequestMessage(tx.Location, HttpMethod.Post);
        var response = await httpClient.SendAsync(req);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("Atomic-Expires", out var values))
        {
            tx.Expires = DateTime.Parse(values.First());
        }
    }

    public async Task CommitTransaction(Transaction tx)
    {
        HttpRequestMessage req = MakeHttpRequestMessage(tx.Location, HttpMethod.Put);
        var response = await httpClient.SendAsync(req);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                tx.Committed = true;
                break;
            case HttpStatusCode.NotFound:
                // error?
                break;
            case HttpStatusCode.Conflict:
                tx.Committed = false;
                break;
            case HttpStatusCode.Gone:
                tx.Expired = true;
                break;
            default:
                // error?
                break;
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task RollbackTransaction(Transaction tx)
    {
        HttpRequestMessage req = MakeHttpRequestMessage(tx.Location, HttpMethod.Delete);
        var response = await httpClient.SendAsync(req);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                tx.RolledBack = true;
                break;
            case HttpStatusCode.NotFound:
                // error?
                break;
            case HttpStatusCode.Gone:
                tx.Expired = true;
                break;
            default:
                // error?
                break;
        }
        response.EnsureSuccessStatusCode();
    }
}