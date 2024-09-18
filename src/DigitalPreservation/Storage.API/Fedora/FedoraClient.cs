using Storage.Repository.Common;

namespace Storage.API.Fedora;

internal class FedoraClient(HttpClient httpClient, ILogger<FedoraClient> logger) : IFedoraClient
{
    public async Task<ConnectivityCheckResult> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync("./fcr:systeminfo", cancellationToken);
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd,
                Success = true
            };
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
}