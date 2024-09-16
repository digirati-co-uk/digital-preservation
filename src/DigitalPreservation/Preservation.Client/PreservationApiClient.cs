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

    public async Task<bool> CanTalkToS3(CancellationToken cancellationToken)
    {
        try
        {
            var res = await httpClient.GetAsync("/storage/check-s3", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Preservation API can see S3");
            return false;
        }
    }

    public async Task<bool> CanSeeThatStorageCanTalkToS3(CancellationToken cancellationToken)
    {
        try
        {
            var res = await httpClient.GetAsync("/storage/check-storage-s3", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Preservation API can see that Storage API can see S3");
            return false;
        }
    }
}