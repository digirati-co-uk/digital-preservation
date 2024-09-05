namespace Storage.API.Fedora;

internal class FedoraClient(HttpClient httpClient, ILogger<FedoraClient> logger) : IFedoraClient
{
    public async Task<bool> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync("./fcr:systeminfo", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Fedora is alive");
            return false;
        }
    }
}