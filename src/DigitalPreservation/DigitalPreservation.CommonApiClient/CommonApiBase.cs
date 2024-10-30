using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
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

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return Result.Fail<PreservedResource?>(ErrorCodes.NotFound, "No resource at " + uri);
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<PreservedResource?>(ErrorCodes.Unauthorized, "Unauthorized for " + uri);
                default:
                    return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, "Status " + response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, e.Message);
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

            switch (response.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    return Result.Fail<Container?>(ErrorCodes.Conflict, "Conflicting resource at " + uri);
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<Container?>(ErrorCodes.Unauthorized, "Unauthorized for " + uri);
                case HttpStatusCode.BadRequest:
                    return Result.Fail<Container?>(ErrorCodes.BadRequest, "Bad Request");
                default:
                    return Result.Fail<Container?>(ErrorCodes.UnknownError, "Status " + response.StatusCode);
            }
            
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Container?>(ErrorCodes.UnknownError, e.Message);
        }
    }
}