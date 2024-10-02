using System.Net;
using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Caching.Memory;
using Storage.API.Fedora.Http;
using Storage.API.Fedora.Model;
using Storage.API.Fedora.Vocab;
using Storage.Repository.Common;

namespace Storage.API.Fedora;

internal class FedoraClient(
    HttpClient httpClient,
    ILogger<FedoraClient> logger,
    Converters converters,
    IMemoryCache cache,
    IStorageMapper storageMapper) : IFedoraClient
{
    public async Task<Result<PreservedResource?>> GetResource(string? pathUnderFedoraRoot, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var uri = converters.GetFedoraUri(pathUnderFedoraRoot);
        var typeRes = await GetResourceType(pathUnderFedoraRoot, transaction);
        if (!typeRes.Success)
        {
            return Result.Fail<PreservedResource?>(typeRes.ErrorCode ?? ErrorCodes.UnknownError, typeRes.ErrorMessage);
        }
        
        if (typeRes.Value == nameof(ArchivalGroup))
        {
            var agResult = await GetPopulatedArchivalGroup(pathUnderFedoraRoot!, null, transaction);
            if (agResult.Success)
            {
                return Result.Ok(agResult.Value as PreservedResource);
            }
            return Result.Fail<PreservedResource?>(agResult.ErrorCode ?? ErrorCodes.UnknownError, agResult.ErrorMessage);
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
            var container = await GetPopulatedContainer(uri, false, false, transaction);
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

        if(await GetPopulatedContainer(uri, true, true, transaction) is not ArchivalGroup archivalGroup)
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"{uri} is not an Archival Group");
        }
        if(archivalGroup.Id != converters.ConvertToRepositoryUri(uri))
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"{uri} does not match {archivalGroup.Id}");
        }

        archivalGroup.Origin = storageMapper.GetArchivalGroupOrigin(archivalGroup.Id);
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
        
        var parentUri = resourceUri.GetParentUri();
        while (parentUri != null && !converters.IsFedoraRoot(parentUri))
        {
            // We know the root is not an Archival Group, we can skip testing it.
            testUris.Add(parentUri);
            parentUri = parentUri.GetParentUri();
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
    
    public async Task<Result<Container?>> CreateContainer(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default)
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
        while (parentPath != null)
        {
            var parent = await GetResourceType(parentPath, transaction);
            if (parent is not { Success: true, Value: nameof(Container) }) // i.e., not ArchivalGroup or Binary or whatever
            {
                return Result.Fail<Container?>(ErrorCodes.Conflict,
                    $"Ancestors of {pathUnderFedoraRoot} must all be Containers, {parentPath} is a {parent.Value}.");
            }
            parentPath = parentPath.GetParent();
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

    private async Task<Container?> GetPopulatedContainer(Uri uri, bool isArchivalGroup, bool recurse, Transaction? transaction = null)
    {
        // TODO: This is not using transaction - should it?
        var request = MakeHttpRequestMessage(uri, HttpMethod.Get)
            .ForJsonLd()
            .WithContainedDescriptions();
        
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
            
        // Make a map of the IDs
        Dictionary<string, JsonElement> dict = containerAndContained.ToDictionary(x => x.GetProperty("@id").GetString()!);
        var fedoraObject = containerAndContained[0].Deserialize<FedoraJsonLdResponse>();
        var topContainer = isArchivalGroup ? converters.MakeArchivalGroup(fedoraObject!) : converters.MakeContainer(fedoraObject!);

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
                    container = await GetPopulatedContainer(fedoraContainer.Id, false, true, transaction);
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
}