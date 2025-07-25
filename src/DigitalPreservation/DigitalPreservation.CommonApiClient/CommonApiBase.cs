using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;

using HttpHeaders = DigitalPreservation.Core.Web.Headers.HttpHeaders;

namespace DigitalPreservation.CommonApiClient;

/// <summary>
/// This class contains client code that would be identical in Storage API and Preservation API clients
/// </summary>
/// <param name="httpClient"></param>
/// <param name="logger"></param>
public abstract class CommonApiBase(HttpClient httpClient, ILogger logger)
{

    public async Task<Result<PreservedResource?>> GetResource(string path)
    {
        return await GetResourceInternal(path);
    }

    public async Task<Result<ArchivalGroup?>> GetArchivalGroup(string path)
    {
        logger.LogInformation("Getting ArchivalGroup " + path);
        var result = await GetResourceInternal(path);
        if (result.Success)
        {
            return Result.Ok<ArchivalGroup?>(result.Value as ArchivalGroup);
        }
        logger.LogWarning("Failed to get ArchivalGroup " + path + ": " + result.CodeAndMessage());
        return Result.Cast<PreservedResource?, ArchivalGroup?>(result);
    }

    
    private async Task<Result<PreservedResource?>> GetResourceInternal(string path)
    {        
        // path MUST be the full /repository... path, which we just pass through as-is
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);

            var response = await httpClient.SendAsync(req);
            var stream = await response.Content.ReadAsStreamAsync();
            if (response.IsSuccessStatusCode)
            {
                var parsed = PreservedResourceDeserializer.Parse(stream);
                if (parsed != null)
                {
                    return Result.Ok<PreservedResource?>(parsed);
                }
                return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<PreservedResource>("Unable to GET Resource");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result<PreservedResource?>> GetLightweightResource(string pathUnderRoot, string? version)
    {        
        var reqPath = $"{PreservedResource.BasePathElement}/{pathUnderRoot}?view=lightweight";
        if (version.HasText())
        {
            reqPath = $"{reqPath}&version={version}";
        }
        try
        {
            var uri = new Uri(reqPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            var stream = await response.Content.ReadAsStreamAsync();
            if (response.IsSuccessStatusCode)
            {
                var parsed = PreservedResourceDeserializer.Parse(stream);
                if (parsed != null)
                {
                    return Result.Ok<PreservedResource?>(parsed);
                }
                return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<PreservedResource>("Unable to GET Resource");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<string?>> GetResourceType(string path)
    {     
        // path MUST be the full /repository... path, which we just pass through as-is
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Head, uri);
            var response = await httpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues(HttpHeaders.XPreservationResourceType, out var values))
                {
                    var resourceType = values.FirstOrDefault();
                    if (resourceType != null)
                    {
                        return Result.Ok(resourceType);
                    }
                }
            }

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                return Result.Fail<string?>(ErrorCodes.Tombstone, "Resource has been replaced by a Tombstone.");
            }
            var errorCode = ErrorCodes.GetErrorCode((int?)response.StatusCode);
            return Result.Fail<string?>(errorCode, "Resource Type could not be retrieved.");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<string?>(ErrorCodes.UnknownError, e.Message);
        }
        
    }

    
    
    public async Task<Result<Container?>> CreateContainer(string path, string? name = null)
    {
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            HttpResponseMessage response;
            if (name.HasText())
            {
                var container = new Container { Name = name };
                response = await httpClient.PutAsJsonAsync(uri, container);
            }
            else
            {
                response = await httpClient.PutAsync(uri, null);
            }
            var stream = await response.Content.ReadAsStreamAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var parsed = PreservedResourceDeserializer.Parse(stream);
                if (parsed is Container createdContainer)
                {
                    return Result.Ok<Container?>(createdContainer);
                }
                return Result.Fail<Container?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<Container>("Unable to create Container");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Container?>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result> DeleteContainer(string path, bool purge, CancellationToken cancellationToken)
    {
        // Make this work the same way that Fedora does; surface the tombstone through the two APIs
        // https://wiki.lyrasis.org/display/FEDORA6x/Delete+vs+Purge
        try
        {
            var uri = new Uri($"{path}?purge={purge}", UriKind.Relative);
            var response = await httpClient.DeleteAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Result.Ok();
            }
            return await response.ToFailResult("Unable to delete Container");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result<ArchivalGroup?>> TestArchivalGroupPathInternal(string reqPath)
    {
        var uri = new Uri(reqPath, UriKind.Relative);
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var ag = await response.Content.ReadFromJsonAsync<ArchivalGroup>();
                if(ag != null)
                {
                    return Result.Ok(ag);
                }
                return Result.Fail<ArchivalGroup>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<ArchivalGroup>("Unable to test archival group path");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<ArchivalGroup>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result<StorageMap>> GetStorageMap(string archivalGroupPathUnderRoot, string? version = null)
    {
        var reqPath = $"ocfl/storagemap/{archivalGroupPathUnderRoot}";
        if (version.HasText())
        {
            reqPath = $"{reqPath}?version={version}";
        }
        try
        {
            var uri = new Uri(reqPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            var storageMap = await response.Content.ReadFromJsonAsync<StorageMap>();
            if(storageMap != null)
            {
                logger.LogInformation("Received storageMap for ArchivalGroup {archivalGroupPathUnderRoot}, version {version}",
                    archivalGroupPathUnderRoot, version);
                return Result.OkNotNull(storageMap);
            }
            return Result.FailNotNull<StorageMap>(ErrorCodes.UnknownError, "Resource could not be parsed.");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<StorageMap>(ErrorCodes.UnknownError, e.Message);
        }
    }
}