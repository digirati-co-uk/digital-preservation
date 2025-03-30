using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;

namespace LeedsDlipServices.MVPCatalogueApi;

public class MvpCatalogue(HttpClient httpClient, IOptions<CatalogueOptions> catalogueOptions) : IMvpCatalogue
{
    private readonly string template = catalogueOptions.Value.QueryTemplate;
    
    public async Task<Result<CatalogueRecord>> GetCatalogueRecordByPid(string pid, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri($"{template}{pid}", UriKind.Relative);
            var response = await httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var pidResult = await response.Content.ReadFromJsonAsync<PidResult>(cancellationToken: cancellationToken);
                if (pidResult == null)
                {
                    return Result.FailNotNull<CatalogueRecord>(ErrorCodes.UnknownError, "Unable to deserialize PidResult response");
                }
                if (pidResult.Error.HasText())
                {
                    return Result.FailNotNull<CatalogueRecord>(ErrorCodes.UnknownError, pidResult.Error);
                }
                if (pidResult.Data == null)
                {
                    return Result.FailNotNull<CatalogueRecord>(ErrorCodes.UnknownError, $"No data returned for Pid: {pid}");
                }
                
                return Result.OkNotNull(pidResult.Data);
                
            }

            var errorCode = ErrorCodes.GetErrorCode((int?)response.StatusCode);
            return Result.FailNotNull<CatalogueRecord>(errorCode, 
                $"MVP Catalogue Service returned {response.StatusCode} for pid {pid}, {response.ReasonPhrase ?? "(no reason given)"}");
        }
        catch (Exception e)
        {
            return Result.FailNotNull<CatalogueRecord>(ErrorCodes.UnknownError, e.Message);
        }
    }
}