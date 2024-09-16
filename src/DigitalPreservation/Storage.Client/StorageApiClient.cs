using Microsoft.Extensions.Logging;

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
    public async Task<bool> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync("/fedora", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if API is alive");
            return false;
        }
    }

    public async Task<bool> CanSeeS3(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync("/storagecheck", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if API is alive");
            return false;
        }
    }
}