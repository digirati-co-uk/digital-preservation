namespace Preservation.Client;

internal class PreservationApiClient(HttpClient httpClient) : IPreservationApiClient
{
    public async Task<bool> IsAlive(CancellationToken cancellationToken = default)
    {
        var res = await httpClient.GetAsync("/storage", cancellationToken);
        return res.IsSuccessStatusCode;
    }
}