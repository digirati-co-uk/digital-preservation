using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;

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
        return await GetResourceInternal(path, null);
    }

    public async Task<Result<ArchivalGroup?>> GetArchivalGroup(string path, string? version)
    {
        var result = await GetResourceInternal(path, version);
        if (result.Success)
        {
            return Result.Ok<ArchivalGroup?>(result.Value as ArchivalGroup);
        }
        return Result.Cast<PreservedResource?, ArchivalGroup?>(result);
    }
    
    private async Task<Result<PreservedResource?>> GetResourceInternal(string path, string? version)
    {        
        // path MUST be the full /repository... path, which we just pass through as-is
        try
        {
            if(!string.IsNullOrWhiteSpace(version))
            {
                path += "?version=" + version;
            }
            var uri = new Uri(path, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            var stream = await response.Content.ReadAsStreamAsync();
            if (response.IsSuccessStatusCode)
            {
                var parsed = Deserializer.Parse(stream);
                if (parsed != null)
                {
                    return Result.Ok<PreservedResource?>(parsed);
                }
                return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<PreservedResource>();
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
            var errorCode = ProblemDetailsX.GetErrorCode((int?)response.StatusCode);
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
                var parsed = Deserializer.Parse(stream);
                if (parsed is Container createdContainer)
                {
                    return Result.Ok<Container?>(createdContainer);
                }
                return Result.Fail<Container?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<Container>();
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
            return await response.ToFailResult();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }
}