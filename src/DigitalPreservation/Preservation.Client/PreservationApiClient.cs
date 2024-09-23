using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Preservation.Client;

internal class PreservationApiClient(HttpClient httpClient, ILogger<PreservationApiClient> logger) : IPreservationApiClient
{
    public async Task<PreservedResource?> GetResource(string path)
    {
        // path MUST be the full /repository... path, which we just pass through as-is
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
            var res = await httpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storage", cancellationToken);
            return res;
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

    public async Task<ConnectivityCheckResult?> CanTalkToS3(CancellationToken cancellationToken)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storage/check-s3", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Preservation API can see S3");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.PreservationApiReadS3,
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<ConnectivityCheckResult?> CanSeeThatStorageCanTalkToS3(CancellationToken cancellationToken)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storage/check-storage-s3", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Preservation API can see that Storage API can see S3");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.StorageApiReadS3,
                Success = false,
                Error = ex.Message
            };
        }
    }
}