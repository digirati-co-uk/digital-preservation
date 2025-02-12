using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Export;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Web;
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
public class StorageApiClient(
    HttpClient httpClient, 
    ILogger<StorageApiClient> logger) : CommonApiBase(httpClient, logger), IStorageApiClient
{
    private readonly HttpClient storageHttpClient = httpClient;

    public async Task<Result<ArchivalGroup?>> TestArchivalGroupPath(string archivalGroupPathUnderRoot)
    {
        var reqPath = $"import/test-path/{archivalGroupPathUnderRoot}";
        return await TestArchivalGroupPathInternal(reqPath);
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
            return await response.ToFailNotNullResult<ImportJobResult>();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }
 
    public async Task<Result<ImportJobResult>> ExecuteImportJob(ImportJob? requestImportJob, CancellationToken cancellationToken = default)
    {
        if (requestImportJob == null)
        {
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, "Unable to parse storage import job from request body.");
        }
        try
        {
            var response = await storageHttpClient.PostAsJsonAsync("import", requestImportJob, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var importJobResult = await response.Content.ReadFromJsonAsync<ImportJobResult>(cancellationToken: cancellationToken);
                if(importJobResult != null)
                {
                    return Result.OkNotNull(importJobResult);
                }
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailNotNullResult<ImportJobResult>();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<Export>> ExportArchivalGroup(
        Uri archivalGroup, 
        Uri exportLocation,
        string versionToExport,
        CancellationToken cancellationToken = default)
    {
        var result = await ExportArchivalGroupBase("export", 
            archivalGroup, exportLocation, versionToExport, cancellationToken);
        return result;
    }

    public async Task<Result<Export>> ExportArchivalGroupMetsOnly(Uri archivalGroup, Uri exportLocation, string? versionToExport,
        CancellationToken cancellationToken = default)
    {
        var result = await ExportArchivalGroupBase("exportMetsOnly", 
            archivalGroup, exportLocation, versionToExport, cancellationToken);
        return result;
    }


    private async Task<Result<Export>> ExportArchivalGroupBase(
        string pathElement,
        Uri archivalGroup,
        Uri exportLocation,
        string? versionToExport,
        CancellationToken cancellationToken = default)
    {        
        var export = new Export
        {
            ArchivalGroup = archivalGroup,
            Destination = exportLocation,
            SourceVersion = versionToExport
        };
        try
        {
            var response = await storageHttpClient.PostAsJsonAsync(pathElement, export, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var exportResult = await response.Content.ReadFromJsonAsync<Export>(cancellationToken: cancellationToken);
                if(exportResult != null)
                {
                    return Result.OkNotNull(exportResult);
                }
                return Result.FailNotNull<Export>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailNotNullResult<Export>();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<Export>(ErrorCodes.UnknownError, e.Message);
        }
    }


    public async Task<Result<Export>> GetExport(Uri entityExportResultUri)
    {     
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, entityExportResultUri);
            var response = await storageHttpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var export = await response.Content.ReadFromJsonAsync<Export>();
                if(export != null)
                {
                    return Result.OkNotNull(export);
                }
                return Result.FailNotNull<Export>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailNotNullResult<Export>();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<Export>(ErrorCodes.UnknownError, e.Message);
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