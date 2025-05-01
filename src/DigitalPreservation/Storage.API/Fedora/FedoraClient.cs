using System.Net;
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
    private readonly bool requireChecksum = fedoraOptions.Value.RequireDigestOnBinary;
    private readonly string fedoraBucket = fedoraOptions.Value.Bucket;
    
    public async Task<Result<PreservedResource?>> GetResource(string? pathUnderFedoraRoot, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var uri = converters.GetFedoraUri(pathUnderFedoraRoot);
        var typeRes = await GetResourceType(pathUnderFedoraRoot, transaction);
        if (!typeRes.Success)
        {
            return Result.ConvertFail<string?, PreservedResource?>(typeRes);
        }

        if (typeRes.Value == nameof(RepositoryTypes.Tombstone))
        {
            return Result.Fail<PreservedResource?>(ErrorCodes.Tombstone, 
                $"The resource at {pathUnderFedoraRoot} has been replaced by a Tombstone");
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
                canUseDb: storageMap == null);
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
        return await GetPopulatedArchivalGroup(uri, version);
    }

    private async Task<Result<ArchivalGroup?>> GetPopulatedArchivalGroup(Uri uri, string? version = null)
    {
        var versions = await GetFedoraVersions(uri);
        if (versions == null)
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"No versions found for {uri}");
        }
        var storageMap = await GetCacheableStorageMap(uri, version, true);
        MergeVersions(versions, storageMap.AllVersions);

        if(await GetPopulatedContainer(uri, true, true, false) is not ArchivalGroup archivalGroup)
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"{uri} is not an Archival Group");
        }
        if(archivalGroup.Id != converters.ConvertToRepositoryUri(uri))
        {
            return Result.Fail<ArchivalGroup?>(ErrorCodes.UnknownError,$"{uri} does not match {archivalGroup.Id}");
        }

        archivalGroup.Origin = storageMapper.GetArchivalGroupOrigin(archivalGroup.Id)!.S3UriInBucket(fedoraBucket);
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

        var message = await headResponse.Content.ReadAsStringAsync();
        return Result.Fail<string?>(ErrorCodes.GetErrorCode((int?)headResponse.StatusCode), message);
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
        if (existing.ErrorCode == ErrorCodes.Tombstone)
        {
            return Result.Fail<Container?>(ErrorCodes.Tombstone,
                $"Tombstone exists at {pathUnderFedoraRoot} ({existing.Value})");
        }
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

    public async Task<Result<Container?>> CreateContainer(string pathUnderFedoraRoot, string callerIdentity, string? name,
        Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        return await CreateContainerWithChecks(pathUnderFedoraRoot, callerIdentity, name, false, transaction);
    }

    public async Task<Result<Container?>> CreateContainerWithinArchivalGroup(string pathUnderFedoraRoot, string callerIdentity, string? name, Transaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateContainerWithChecks(pathUnderFedoraRoot, callerIdentity, name, true, transaction);
    }
    
    private async Task<Result<Container?>> CreateContainerWithChecks(string pathUnderFedoraRoot, string callerIdentity, string? name, bool isWithinArchivalGroup, Transaction? transaction = null)
    {
        var validateResult = await ContainerCanBeCreatedAtPath(pathUnderFedoraRoot, isWithinArchivalGroup, transaction);
        if (validateResult.Failure)
        {
            return validateResult;
        }
        // All tests passed
        var containerResult = await CreateContainerInternal(false, pathUnderFedoraRoot, callerIdentity, name, transaction);
        return containerResult;
    }


    private async Task<Result<Container?>> CreateContainerInternal(
        bool asArchivalGroup,
        string pathUnderFedoraRoot,
        string callerIdentity,
        string? name,
        Transaction? transaction = null)
    {
        // TODO: This follows the prototype and performs a POST, adding a _desired_ slug.
        // Investigate whether a PUT would be better (or worse)
        var fedoraUri = converters.GetFedoraUri(pathUnderFedoraRoot);
        
        var req = MakeHttpRequestMessage(fedoraUri.GetParentUri()!, HttpMethod.Post)
            .InTransaction(transaction)
            .WithName(name)
            .WithCreatedBy(callerIdentity)
            .WithSlug(fedoraUri.GetSlug()!);
        if (asArchivalGroup)
        {
            req.AsArchivalGroup();
        }
        var response = await httpClient.SendAsync(req);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync();
            return Result.Fail<Container>(ErrorCodes.GetErrorCode((int?)response.StatusCode), message);    
        }

        if(asArchivalGroup)
        {
            // TODO - could combine this with .WithName(name) and make a more general .WithRdf(string name = null, string type = null)
            // so that the extra info goes on the initial POST and this little PATCH isn't required
            var patchReq = MakeHttpRequestMessage(response.Headers.Location!, HttpMethod.Patch)
                .InTransaction(transaction);
            // We give the AG this _additional_ type so that we can see that it's an AG when we get bulk child members back from .WithContainedDescriptions
            patchReq.AsInsertTypePatch("<http://purl.org/dc/dcmitype/Collection>", callerIdentity);
            var patchResponse = await httpClient.SendAsync(patchReq);
            if (!patchResponse.IsSuccessStatusCode)
            {
                return Result.Fail<Container>(ErrorCodes.GetErrorCode((int?)patchResponse.StatusCode), 
                    "Could not PATCH Container: " + patchResponse.ReasonPhrase);
            }
        }
        // The body is the new resource URL
        var newReq = MakeHttpRequestMessage(response.Headers.Location!, HttpMethod.Get)
            .InTransaction(transaction)
            .ForJsonLd();
        var newResponse = await httpClient.SendAsync(newReq);

        var containerResponse = await MakeFedoraResponse<FedoraJsonLdResponse>(newResponse);
        if (containerResponse == null)
        {
            return Result.Fail<Container>(ErrorCodes.UnknownError, "Could not deserialise Fedora response for new Container");
        }
        var container = asArchivalGroup ? converters.MakeArchivalGroup(containerResponse) : converters.MakeContainer(containerResponse);
        return Result.Ok(container);
    }

    public async Task<Result> UpdateContainerMetadata(string pathUnderFedoraRoot, string? name, string callerIdentity,
        Transaction transaction,  CancellationToken cancellationToken = default)
    {
        var existing = await GetResourceType(pathUnderFedoraRoot, transaction);
        if (existing.Failure || existing.Value is not (nameof(Container) or nameof(ArchivalGroup)))
        {
            return Result.Fail(ErrorCodes.BadRequest, $"Resource at {pathUnderFedoraRoot} is not a Container or ArchivalGroup");
        }
        var fedoraUri = converters.GetFedoraUri(pathUnderFedoraRoot);
        var req = MakeHttpRequestMessage(fedoraUri, HttpMethod.Patch)
            .InTransaction(transaction)
            .WithContainerMetadataUpdate(name, callerIdentity);

        var response = await httpClient.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Result.Ok();
    }

    public async Task<Result<Binary?>> PutBinary(Binary binary, string callerIdentity, Transaction transaction, CancellationToken cancellationToken = default)
    {      
        logger.LogInformation("PutBinary {id}", binary.Id);
        // TODO: Can we PUT the resource and set its dc:title in the same PUT request?
        // At the moment we have to make a separate update to the metadata endpoint.
        // verify that parent is a container first?
        var checksumResult = await EnsureChecksum(binary, false);
        if (checksumResult.Failure)
        {
            return Result.Fail<Binary>(ErrorCodes.BadRequest, "No checksum obtainable for binary " + binary.Id);
        }
        logger.LogInformation("Binary {path} has checksum {checksum}", binary.Id!.AbsolutePath, binary.Digest);
        HttpRequestMessage? req;
        HttpResponseMessage? response;
        try
        {
            req = await MakeBinaryPut(binary, transaction);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to Make a binary PUT request");
            return Result.Fail<Binary>(ErrorCodes.UnknownError, "Unable to Make a binary PUT request: " + e.Message);
        }

        if (req == null)
        {
            logger.LogError("HttpRequestMessage for binary PUT is null");
            return Result.Fail<Binary>(ErrorCodes.UnknownError, "HttpRequestMessage for binary PUT is null");
        }
        try
        {
            response = await httpClient.SendAsync(req, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to Send Binary PUT request");
            return Result.Fail<Binary>(ErrorCodes.UnknownError, "Unable to Send Binary PUT request: " + e.Message);
        }
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            // https://github.com/fcrepo/fcrepo/pull/2044
            // see also https://github.com/whikloj/fcrepo4-tests/blob/fcrepo-6/archival_group_tests.py#L149-L190
            // 410 indicates that this URI has a tombstone sitting at it; it has previously been deleted.
            // But we want to reinstate a binary.

            // Log or record somehow that this has happened?
            var retryReq = (await MakeBinaryPut(binary, transaction))?
                .OverwriteTombstone();
            if (retryReq == null)
            {
                logger.LogError("HttpRequestMessage for binary Retry PUT is null");
                return Result.Fail<Binary>(ErrorCodes.UnknownError, "HttpRequestMessage for binary Retry PUT is null");
            }
            response = await httpClient.SendAsync(retryReq, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Response from Fedora was {status}", response.StatusCode);
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            var code = ErrorCodes.GetErrorCode((int?)response.StatusCode);
            return Result.Fail<Binary>(code, $"PUT {binary.GetSlug()} failed; response from Fedora was {response.StatusCode}: {message}");
        }
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
            var isCreation = binaryResponse.CreatedBy == fedoraOptions.Value.AdminUser;
            patchReq.AsInsertTitlePatch(binary.Name!, callerIdentity, isCreation);
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
            if (isMissing && requireChecksum)
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

    private async Task<HttpRequestMessage?> MakeBinaryPut(Binary binary, Transaction transaction)
    {
        logger.LogInformation("Constructing a Binary PUT for {path}", binary.Id!.AbsolutePath);
        var fedoraLocation = converters.GetFedoraUri(binary.Id.GetPathUnderRoot());
        var req = MakeHttpRequestMessage(fedoraLocation, HttpMethod.Put)
            .InTransaction(transaction)
            .WithDigest(binary.Digest, "sha-256"); // move algorithm choice to constant

        // This could instead reference the file in S3, for Fedora to fetch
        // https://fedora-project.slack.com/archives/C8B5TSR4J/p1710164226000799
        // ^ not possible rn - but can use a signed HTTP url to fetch! (TODO)
        
        Stream putStream;
        try
        {
            logger.LogInformation("Getting stream from storage content at origin {origin}", binary.Origin);
            var streamResult = await storage.GetStream(binary.Origin!);
            if (streamResult is { Success: true, Value: not null })
            {
                putStream = streamResult.Value;
            }
            else
            {
                logger.LogError("Unable to read origin as stream: " + streamResult.CodeAndMessage());
                return null;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to read origin as stream");
            return null;
        }        
        try
        {
            logger.LogInformation("Creating StreamContent");
            req.Content = new StreamContent(putStream)
                .WithContentType(binary.ContentType);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to construct StreamContent");
            return null;
        }
       
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
    /// <param name="callerIdentity"></param>
    /// <param name="transaction"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<Result<PreservedResource>> Delete(PreservedResource resource, string callerIdentity, Transaction transaction, CancellationToken cancellationToken = default)
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
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => Result.FailNotNull<PreservedResource>(ErrorCodes.NotFound, "Not found: " + message),
                HttpStatusCode.Unauthorized => Result.FailNotNull<PreservedResource>(ErrorCodes.Unauthorized, "Unauthorized: " + message),
                _ => Result.FailNotNull<PreservedResource>(ErrorCodes.UnknownError, "Fedora returned " + response.StatusCode + ": " + message)
            };
        }
        catch (Exception e)
        {
            return Result.FailNotNull<PreservedResource>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> DeleteContainerOutsideOfArchivalGroup(string pathUnderFedoraRoot, string callerIdentity, bool purge,
        CancellationToken cancellationToken)
    {
        // GetResource can return a tombstone... - check that
        // Now get the resource and make sure it's an EMPTY container OUTSIDE an AG
        var resType = await GetResourceType(pathUnderFedoraRoot);
        if (resType.Failure)
        {
            return Result.Fail(resType.ErrorCode ?? ErrorCodes.UnknownError, resType.ErrorMessage);
        }

        var fedoraLocation = converters.GetFedoraUri(pathUnderFedoraRoot);
        if (resType.Value == nameof(RepositoryTypes.Tombstone))
        {
            // already a tombstone
            if (purge)
            {
                return await PurgeTombstone(cancellationToken, fedoraLocation);
            }
            return Result.Fail(ErrorCodes.Tombstone, "Already a tombstone but purge flag not set");
        }
        // Not yet a tombstone

        if (resType.Value != nameof(Container))
        {
            return Result.Fail(ErrorCodes.BadRequest, "Attempted to delete a resource that is not a Container");
        }
        
        // OK, so it's a Container - but now we need to validate that it's a deletable container
        var container = await GetPopulatedContainer(fedoraLocation, false, false, false);
        if (container == null || container.PartOf != null || container.Containers.Count > 0 || container.Binaries.Count > 0)
        {
            return Result.Fail(ErrorCodes.BadRequest, "This is not a deletable container");
        }
        
        // It's OK to attempt to delete (and optionally purge) this Container
        try
        {
            // delete the resource
            var response = await httpClient.DeleteAsync(fedoraLocation, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (purge)
                {
                    return await PurgeTombstone(cancellationToken, fedoraLocation);
                }
            }
            else
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Fail(ErrorCodes.GetErrorCode((int?)response.StatusCode), message);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ErrorCodes.UnknownError, ex.Message);
        }

        return Result.Fail(ErrorCodes.UnknownError);
    }

    private async Task<Result> PurgeTombstone(CancellationToken cancellationToken, Uri fedoraLocation)
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
            var message = await delTombstoneResponse.Content.ReadAsStringAsync(cancellationToken);
            return Result.Fail(ErrorCodes.GetErrorCode((int?)delTombstoneResponse.StatusCode), "Failed to purge tombstone: " + message);
        }

        return Result.Fail(ErrorCodes.UnknownError, "Attempted to purge something that is not a tombstone");
    }

    public async Task<Result<ArchivalGroup?>> CreateArchivalGroup(string pathUnderFedoraRoot, string callerIdentity, string name, Transaction transaction, CancellationToken cancellationToken = default)
    {
        var agResult = await CreateContainerInternal(true, pathUnderFedoraRoot, callerIdentity, name, transaction);
        if (agResult is { Success: true, Value: ArchivalGroup })
        {
            return Result.Ok((ArchivalGroup)agResult.Value);
        }
        return Result.Fail<ArchivalGroup>(agResult.ErrorCode ?? ErrorCodes.UnknownError, agResult.ErrorMessage);
    }

    private async Task<Container?> GetPopulatedContainer(Uri uri, bool isArchivalGroup, bool recurse, bool canUseDb)
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
        if (containerAndContained[0].GetProperty("@id").GetString() != uri.GetStringTemporaryForTesting())
        {
            throw new InvalidOperationException("First resource in @graph should be the asked-for URI");
        }

        var jsonElement = containerAndContained[0];
        var fedoraObject = GetFedoraJsonLdResponse(jsonElement);
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
                var fedoraContainer = GetFedoraJsonLdResponse(resource)!;
                Container? container;
                if (recurse)
                {
                    container = await GetPopulatedContainer(fedoraContainer.Id, false, true, canUseDb);
                }
                else
                {
                    container = converters.MakeContainer(fedoraContainer);
                }
                topContainer.Containers.Add(container!);
            }
            else if (resource.HasType("fedora:Binary"))
            {
                var fedoraBinary = GetFedoraBinaryMetadataResponse(resource)!;
                var binary = converters.MakeBinary(fedoraBinary);
                topContainer.Binaries.Add(binary);
            }
        }
        return topContainer;
    }

    private BinaryMetadataResponse? GetFedoraBinaryMetadataResponse(JsonElement jsonElement)
    {
        var titles = GetMultipleDcTitles(jsonElement);
        var binaryMetadata = jsonElement.Deserialize<BinaryMetadataResponse>();
        if (binaryMetadata != null)
        {
            binaryMetadata.Titles = titles;
        }

        return binaryMetadata;
    }

    /// <summary>
    /// The fedora title object can be a single string, or an array of strings.
    /// </summary>
    /// <param name="jsonElement"></param>
    /// <returns></returns>
    private static FedoraJsonLdResponse? GetFedoraJsonLdResponse(JsonElement jsonElement)
    {
        var titles = GetMultipleDcTitles(jsonElement);
        var fedoraObject = jsonElement.Deserialize<FedoraJsonLdResponse>();
        if (fedoraObject != null)
        {
            fedoraObject.Titles = titles;
        }

        return fedoraObject;
    }

    private static List<string> GetMultipleDcTitles(JsonElement jsonElement)
    {
        List<string> titles = [];
        if (jsonElement.TryGetProperty("title", out JsonElement titleElement)) 
        {
            if (titleElement.ValueKind == JsonValueKind.String)
            {
                titles.Add(titleElement.GetString()!);
            }
            else if (titleElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var titleStringElement in titleElement.EnumerateArray())
                {
                    titles.Add(titleStringElement.GetString()!);
                }
            }
        }

        return titles;
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
        
        var stream = await response.Content.ReadAsStreamAsync();
        using var jDoc = await JsonDocument.ParseAsync(stream); // jDoc is a Fedora JSON response
        var titles = GetMultipleDcTitles(jDoc.RootElement);
        var fedoraResponse = jDoc.Deserialize<T>();
        if (fedoraResponse != null)
        {
            fedoraResponse.Titles = titles;
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
        // But... the Fedora API gives the created and lastModified date of the original, not the version, when you ask for a versioned response.
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
        }
        response.EnsureSuccessStatusCode();
    }
}