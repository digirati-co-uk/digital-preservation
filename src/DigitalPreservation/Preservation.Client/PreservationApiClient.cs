using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Preservation.Client;

internal class PreservationApiClient(
    HttpClient httpClient,
    ILogger<PreservationApiClient> logger) : CommonApiBase(httpClient, logger), IPreservationApiClient
{
    private readonly HttpClient preservationHttpClient = httpClient;

    public async Task<Result<Deposit?>> CreateDeposit(
        string? archivalGroupRepositoryPath,
        string? archivalGroupProposedName,
        string? submissionText,
        bool useObjectTemplate,
        CancellationToken cancellationToken = default)
    {
        var deposit = new Deposit
        {
            ArchivalGroup = archivalGroupRepositoryPath.HasText() ? new Uri(preservationHttpClient.BaseAddress!, archivalGroupRepositoryPath) : null,
            ArchivalGroupName = archivalGroupProposedName,
            SubmissionText = submissionText,
            UseObjectTemplate = useObjectTemplate
        };
        try
        {
            var uri = new Uri($"/{Deposit.BasePathElement}", UriKind.Relative);
            HttpResponseMessage response = await preservationHttpClient.PostAsJsonAsync(uri, deposit, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var createdDeposit = await response.Content.ReadFromJsonAsync<Deposit>(cancellationToken: cancellationToken);
                if (createdDeposit is not null)
                {
                    return Result.Ok(createdDeposit);
                }
                return Result.Fail<Deposit>(ErrorCodes.UnknownError, "No deposit returned");
            }
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<Deposit>(ErrorCodes.Unauthorized, "Unauthorized for creating new deposits");
                case HttpStatusCode.BadRequest:
                    return Result.Fail<Deposit>(ErrorCodes.BadRequest, "Bad Request");
                default:
                    return Result.Fail<Deposit>(ErrorCodes.UnknownError, "Status " + response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<List<Deposit>>> GetDeposits(DepositQuery? query, CancellationToken cancellationToken = default)
    {        
        try
        {
            var relPath = "/deposits";
            var queryString = QueryBuilder.MakeQueryString(query);
            if (queryString.HasText())
            {
                relPath += $"?{queryString}";   
            }
            var uri = new Uri(relPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var deposits = await response.Content.ReadFromJsonAsync<List<Deposit>>(cancellationToken: cancellationToken);
                    if (deposits is not null)
                    {
                        return Result.OkNotNull(deposits);
                    }
                    return Result.FailNotNull<List<Deposit>>(ErrorCodes.NotFound, "No resource at " + uri);
                case HttpStatusCode.NotFound:
                    return Result.FailNotNull<List<Deposit>>(ErrorCodes.NotFound, "No resource at " + uri);
                case HttpStatusCode.Unauthorized:
                    return Result.FailNotNull<List<Deposit>>(ErrorCodes.Unauthorized, "Unauthorized for " + uri);
                default:
                    return Result.FailNotNull<List<Deposit>>(ErrorCodes.UnknownError, "Status " + response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<Deposit>>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<Deposit?>> GetDeposit(string id, CancellationToken cancellationToken = default)
    {        
        try
        {
            var relPath = $"/deposits/{id}";
            var uri = new Uri(relPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var deposit = await response.Content.ReadFromJsonAsync<Deposit>(cancellationToken: cancellationToken);
                    if (deposit is not null)
                    {
                        return Result.Ok(deposit);
                    }
                    return Result.Fail<Deposit>(ErrorCodes.NotFound, "No resource at " + uri);
                case HttpStatusCode.NotFound:
                    return Result.Fail<Deposit>(ErrorCodes.NotFound, "No resource at " + uri);
                case HttpStatusCode.Unauthorized:
                    return Result.Fail<Deposit>(ErrorCodes.Unauthorized, "Unauthorized for " + uri);
                default:
                    return Result.Fail<Deposit>(ErrorCodes.UnknownError, "Status " + response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
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