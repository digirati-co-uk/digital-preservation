using DigitalPreservation.Common.Model;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;

namespace Storage.API.Fedora;

internal class FedoraClient(HttpClient httpClient, ILogger<FedoraClient> logger) : IFedoraClient
{
    public Task<PreservedResource?> GetResource(Uri uri, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        // See proto FedoraWrapper:514 and 779
        // First pass, assume it is a container. And don't find parent AG.
        // Later we'll need to interrogate
        throw new NotImplementedException();
    }


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