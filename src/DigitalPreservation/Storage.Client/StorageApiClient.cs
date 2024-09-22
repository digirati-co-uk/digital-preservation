using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
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
    public async Task<PreservedResource?> GetResource(string path)
    {
        var uri = new Uri(path, UriKind.Relative);
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await httpClient.SendAsync(req);
        var stream = await response.Content.ReadAsStreamAsync();
        var parsed = Deserializer.Parse(stream);
        if (parsed != null)
        {
            return parsed;
        }
        // TODO: Handle missing resource
        return null;
    }

    public async Task<Container?> CreateContainer(string path, string? name = null)
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
        var parsed = Deserializer.Parse(stream);
        if (parsed is Container createdContainer)
        {
            return createdContainer;
        }
        // TODO: Handle missing resource
        return null;
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