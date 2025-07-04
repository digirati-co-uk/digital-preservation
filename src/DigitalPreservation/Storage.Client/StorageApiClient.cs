using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.ChangeDiscovery.Reader;
using DigitalPreservation.Common.Model.Export;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
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

    public async Task<Result<Stream>> GetBinaryStream(string path)
    {
        if (path.StartsWith($"/{PreservedResource.BasePathElement}/"))
        {
            var contentPath = path.Replace($"/{PreservedResource.BasePathElement}/", "/content/");
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(contentPath, UriKind.Relative));
                var response = await storageHttpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync();
                    return Result.OkNotNull(stream);
                    
                }
                return await response.ToFailNotNullResult<Stream>("Unable to get Binary stream");
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
                return Result.FailNotNull<Stream>(ErrorCodes.UnknownError, e.Message);
            }
        }
        return Result.FailNotNull<Stream>(ErrorCodes.UnknownError, "Unable to get stream for binary " + path);
    }

    public async Task<Result<List<Activity>>> GetImportJobActivities(DateTime after, CancellationToken cancellationToken = default)
    {
        var reader = new ActivityStreamReader(storageHttpClient);
        var importJobsUri = new Uri("/activity/importjobs/collection", UriKind.Relative);
        try
        {
            // for now let's just collect these into a list, to be the value of a Result
            List<Activity> activities = [];
            await foreach (var activity in reader.ReadActivityStream(importJobsUri, after).WithCancellation(cancellationToken))
            {
                activities.Add(activity);
            }
            return Result.OkNotNull(activities);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<Activity>>(ErrorCodes.UnknownError, e.Message);
        }
        
    }

    public async Task<Result<ArchivalGroup?>> TestArchivalGroupPath(string archivalGroupPathUnderRoot)
    {
        logger.LogInformation("Testing archivalGroupPathUnderRoot: " + archivalGroupPathUnderRoot);
        var reqPath = $"import/test-path/{archivalGroupPathUnderRoot}";
        return await TestArchivalGroupPathInternal(reqPath);
    }
    
    public async Task<Result<ImportJobResult>> GetImportJobResult(Uri storageApiImportJobResultUri)
    {        
        logger.LogInformation("StorageAPIClient, GetImportJobResult : " + storageApiImportJobResultUri);
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
            return await response.ToFailNotNullResult<ImportJobResult>("Unable to get import job result");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }
 
    public async Task<Result<ImportJobResult>> ExecuteImportJob(ImportJob? requestImportJob, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Storage API Client Executing import job: " + requestImportJob.LogSummary());
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
            return await response.ToFailNotNullResult<ImportJobResult>("Unable to execute import job");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<string?>> GetArchivalGroupName(string archivalGroupPathUnderRoot, string? version = null)
    {
        // version may have to be a memento timestamp rather than an OCFL version
        var reqPath = $"{PreservedResource.BasePathElement}/{archivalGroupPathUnderRoot}?view=lightweight";
        if (version.HasText())
        {
            reqPath = $"{reqPath}&version={version}";
        }
        try
        {
            var uri = new Uri(reqPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            var container = await response.Content.ReadFromJsonAsync<Container>();
            if(container != null)
            {
                logger.LogInformation("Received container to read name of ArchivalGroup {archivalGroupPathUnderRoot}, version {version}",
                    archivalGroupPathUnderRoot, version);
                return Result.Ok(container.Name);
            }
            return Result.Fail<string>(ErrorCodes.UnknownError, "Resource could not be parsed.");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<string>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<StorageMap>> GetStorageMap(string archivalGroupPathUnderRoot, string? version = null)
    {
        var reqPath = $"ocfl/storagemap/{archivalGroupPathUnderRoot}";
        if (version.HasText())
        {
            reqPath = $"{reqPath}?version={version}";
        }
        try
        {
            var uri = new Uri(reqPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            var storageMap = await response.Content.ReadFromJsonAsync<StorageMap>();
            if(storageMap != null)
            {
                logger.LogInformation("Received storageMap for ArchivalGroup {archivalGroupPathUnderRoot}, version {version}",
                    archivalGroupPathUnderRoot, version);
                return Result.OkNotNull(storageMap);
            }
            return Result.FailNotNull<StorageMap>(ErrorCodes.UnknownError, "Resource could not be parsed.");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<StorageMap>(ErrorCodes.UnknownError, e.Message);
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
        logger.LogInformation("Storage API Client Executing export, archivalGroup: " + archivalGroup);
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
                    logger.LogInformation("Received exportResult with exportResult.ArchivalGroup: " + exportResult.ArchivalGroup);
                    return Result.OkNotNull(exportResult);
                }
                return Result.FailNotNull<Export>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailNotNullResult<Export>("Unable to export archival group");
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
            return await response.ToFailNotNullResult<Export>("Unable to get export result");
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