namespace Storage.API.Fedora;

internal class FedoraClient(HttpClient httpClient) : IFedoraClient
{
    public async Task<bool> IsAlive(CancellationToken cancellationToken = default)
    {
        var res = await httpClient.GetAsync("./fcr:systeminfo", cancellationToken);
        return res.IsSuccessStatusCode;
    }
}