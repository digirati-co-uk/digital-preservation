using System.Net.Http.Json;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.CommonApiClient;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Preservation.Client;

internal class PreservationApiClient(
    HttpClient httpClient,
    ILogger<PreservationApiClient> logger) : CommonApiBase(httpClient, logger), IPreservationApiClient
{
    private readonly HttpClient preservationHttpClient = httpClient;

    public Task<Result<Deposit?>> CreateDeposit(string? archivalGroupPathUnderRoot, string? archivalGroupProposedName, string? submissionText,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await preservationHttpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storage", cancellationToken);
            return res;
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

    public async Task<ConnectivityCheckResult?> CanTalkToS3(CancellationToken cancellationToken)
    {
        try
        {
            var res = await preservationHttpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storage/check-s3", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Preservation API can see S3");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.PreservationApiReadS3,
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<ConnectivityCheckResult?> CanSeeThatStorageCanTalkToS3(CancellationToken cancellationToken)
    {
        try
        {
            var res = await preservationHttpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storage/check-storage-s3", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Preservation API can see that Storage API can see S3");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.StorageApiReadS3,
                Success = false,
                Error = ex.Message
            };
        }
    }
}