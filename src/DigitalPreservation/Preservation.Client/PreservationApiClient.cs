using Microsoft.Extensions.Logging;

namespace Preservation.Client;

internal class PreservationApiClient(HttpClient httpClient, ILogger<PreservationApiClient> logger) : IPreservationApiClient
{
    public async Task<bool> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync("/storage", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Fedora is alive");
            return false;
        }
    }
}