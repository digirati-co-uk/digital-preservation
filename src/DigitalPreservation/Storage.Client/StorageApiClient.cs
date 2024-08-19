namespace Storage.Client;

/// <summary>
/// Concrete implementation of <see cref="IStorageApiClient"/> for interacting with Storage API
/// </summary>
/// <remarks>
/// Marked internal to avoid consumers depending on this implementation, which will allow us to alter how it's
/// implemented in the future to use, e.g. Refit client instead
/// </remarks>
internal class StorageApiClient(HttpClient httpClient) : IStorageApiClient
{
    public async Task<bool> IsAlive(CancellationToken cancellationToken = default)
    {
        var res = await httpClient.GetAsync("/", cancellationToken);
        return res.IsSuccessStatusCode;
    }
}