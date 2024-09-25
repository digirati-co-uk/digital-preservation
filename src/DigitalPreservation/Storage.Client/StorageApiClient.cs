using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Storage.Client;

/// <summary>
/// Concrete implementation of <see cref="IStorageApiClient"/> for interacting with Storage API
/// </summary>
/// <remarks>
/// Marked internal to avoid consumers depending on this implementation, which will allow us to alter how it's
/// implemented in the future to use, e.g. Refit client instead
/// </remarks>
internal class StorageApiClient(
    HttpClient httpClient, 
    ILogger<StorageApiClient> logger) : IStorageApiClient
{
    public async Task<Result<PreservedResource?>> GetResource(string path)
    {
        // THIS CODE IS IDENTICAL TO PreservationApiClient !!!
        // path MUST be the full /repository... path, which we just pass through as-is
        try
        {
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
        // THIS CODE IS IDENTICAL TO PreservationApiClient !!!
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


    public async Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ConnectivityCheckResult>("/fedora", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if API is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd, Success = false
            };
        }
    }

    public async Task<ConnectivityCheckResult?> CanSeeS3(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storagecheck", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if API is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.StorageApiReadS3, Success = false
            };
        }
    }
}