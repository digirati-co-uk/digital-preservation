using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Storage.Client;

/// <summary>
/// Concrete implementation of <see cref="IStorageApiClient"/> for interacting with Storage API
/// </summary>
/// <remarks>
/// Marked internal to avoid consumers depending on this implementation, which will allow us to alter how it's
/// implemented in the future to use, e.g. Refit client instead
/// </remarks>
internal class StorageApiClient(
    HttpClient httpClient, 
    ILogger<StorageApiClient> logger) : CommonApiBase(httpClient, logger), IStorageApiClient
{
    private readonly HttpClient storageHttpClient = httpClient;

    public async Task<Result<ImportJob>> GetImportJob(string archivalGroupPathUnderRoot, Uri sourceUri)
    {       
        var reqPath = $"import/{archivalGroupPathUnderRoot}?source={sourceUri}";
        var uri = new Uri(reqPath, UriKind.Relative);
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await storageHttpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var importJob = await response.Content.ReadFromJsonAsync<ImportJob>();
                if(importJob != null)
                {
                    return Result.OkNotNull(importJob);
                }
                return Result.FailNotNull<ImportJob>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            var message = problemDetails?.Detail ?? problemDetails?.Title;

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return Result.FailNotNull<ImportJob>(ErrorCodes.NotFound, message ?? "No resource at " + uri);
                case HttpStatusCode.Unauthorized:
                    return Result.FailNotNull<ImportJob>(ErrorCodes.Unauthorized, message ?? "Unauthorized for " + uri);
                case HttpStatusCode.UnprocessableContent:
                    return Result.FailNotNull<ImportJob>(ErrorCodes.Unprocessable, message ?? "Probably missing checksum");
                default:
                    return Result.FailNotNull<ImportJob>(ErrorCodes.UnknownError, message ?? "Status " + response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJob>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<ImportJobResult>> GetImportJobResult(Uri storageApiImportJobResultUri)
    {        
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, storageApiImportJobResultUri);
            var response = await storageHttpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var importJobResult = await response.Content.ReadFromJsonAsync<ImportJobResult>();
                if(importJobResult != null)
                {
                    return Result.OkNotNull(importJobResult);
                }
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            var message = problemDetails?.Detail ?? problemDetails?.Title;

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return Result.FailNotNull<ImportJobResult>(ErrorCodes.NotFound, message ?? "No resource at " + storageApiImportJobResultUri);
                case HttpStatusCode.Unauthorized:
                    return Result.FailNotNull<ImportJobResult>(ErrorCodes.Unauthorized, message ?? "Unauthorized for " + storageApiImportJobResultUri);
                // others?
                default:
                    return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, message ?? "Status " + response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }


    public async Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await storageHttpClient.GetFromJsonAsync<ConnectivityCheckResult>("/fedora", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if API is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd, Success = false
            };
        }
    }

    public async Task<ConnectivityCheckResult?> CanSeeS3(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await storageHttpClient.GetFromJsonAsync<ConnectivityCheckResult>("/storagecheck", cancellationToken);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if API is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.StorageApiReadS3, Success = false
            };
        }
    }
}