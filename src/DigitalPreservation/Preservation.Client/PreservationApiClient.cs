using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using LeedsDlipServices.Identity;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Preservation.Client;

internal class PreservationApiClient(
    HttpClient httpClient,
    ILogger<PreservationApiClient> logger) : CommonApiBase(httpClient, logger), IPreservationApiClient
{
    private readonly HttpClient preservationHttpClient = httpClient;



    public async Task<Result<SearchCollection?>> Search(string text, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = new Uri($"/search?text={Uri.EscapeDataString(text)}&pageNumber={page}&pageSize={pageSize}", UriKind.Relative);
            var response = await preservationHttpClient.GetFromJsonAsync<SearchCollection>(uri);
            return Result.OkNotNull(response);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<Result> LockDeposit(Deposit deposit, bool force, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath + "/lock" + (force ? "?force=true" : ""), UriKind.Relative); 
        var response = await preservationHttpClient.PostAsync(uri, null, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result.Ok();
        }
        return await response.ToFailResult("Unable to lock deposit");
    }

    public async Task<Result> ReleaseDepositLock(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath + "/lock", UriKind.Relative); 
        var response = await preservationHttpClient.DeleteAsync(uri, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result.Ok();
        }
        return await response.ToFailResult("Unable to remove lock");
    }

    public async Task<OrderedCollection?> GetOrderedCollection(string stream)
    {
        var uri = new Uri($"/activity/{stream}/collection", UriKind.Relative);
        var oc = await preservationHttpClient.GetFromJsonAsync<OrderedCollection>(uri);
        return oc;
    }

    public async Task<OrderedCollectionPage?> GetOrderedCollectionPage(string stream, int index)
    {
        var uri = new Uri($"/activity/{stream}/pages/{index}", UriKind.Relative);
        var ocp = await preservationHttpClient.GetFromJsonAsync<OrderedCollectionPage>(uri);
        return ocp;
    }


    public async Task<Result<ArchivalGroup?>> TestArchivalGroupPath(string archivalGroupPathUnderRoot)
    {
        var reqPath = $"validation/archivalgroup/{archivalGroupPathUnderRoot}";
        return await TestArchivalGroupPathInternal(reqPath);
    }
    
    public async Task<Result<List<Uri>>> GetAllAgents(CancellationToken cancellationToken)
    {        
        try
        {
            var uri = new Uri("/agents", UriKind.Relative);
            var response = await preservationHttpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var agents = await response.Content.ReadFromJsonAsync<List<Uri>>(cancellationToken: cancellationToken);
                if (agents is not null)
                {
                    return Result.OkNotNull(agents);
                }
                return Result.FailNotNull<List<Uri>>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<List<Uri>>("Unable to get agents");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<Uri>>(ErrorCodes.UnknownError, e.Message);
        }
    }


    public async Task<Result<Deposit?>> UpdateDeposit(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath, UriKind.Relative); 
        try
        {
            HttpResponseMessage response = await preservationHttpClient.PatchAsJsonAsync(uri, deposit, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var patchedDeposit = await response.Content.ReadFromJsonAsync<Deposit>(cancellationToken: cancellationToken);
                if (patchedDeposit is not null)
                {
                    return Result.Ok(patchedDeposit);
                }
                return Result.Fail<Deposit>(ErrorCodes.UnknownError, "No deposit returned");
            }
            return await response.ToFailResult<Deposit>("Unable to update deposit");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> DeleteDeposit(string id, CancellationToken cancellationToken)
    {        
        try
        {
            var relPath = $"/deposits/{id}";
            var uri = new Uri(relPath, UriKind.Relative);
            var response = await preservationHttpClient.DeleteAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Result.Ok();
            }
            return await response.ToFailResult("Unable to delete deposit");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<Deposit?>> CreateDepositFromIdentifier(string schema, string identifier, TemplateType templateType, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/{Deposit.BasePathElement}/from-identifier", UriKind.Relative);
        var body = new SchemaAndValue{ Schema = schema, Value = identifier, Template = templateType };
        try
        {
            HttpResponseMessage response = await preservationHttpClient.PostAsJsonAsync(uri, body, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var createdDeposit = await response.Content.ReadFromJsonAsync<Deposit>(cancellationToken: cancellationToken);
                if (createdDeposit is not null)
                {
                    return Result.Ok(createdDeposit);
                }
                return Result.Fail<Deposit>(ErrorCodes.UnknownError, "No deposit returned");
            }
            return await response.ToFailResult<Deposit>("Unable to create deposit from identifier");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not create deposit");
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<Deposit?>> CreateDeposit(
        string? archivalGroupRepositoryPath,
        string? archivalGroupProposedName,
        string? submissionText,
        TemplateType templateType,
        bool export,
        string? exportVersion,
        CancellationToken cancellationToken = default)
    {
        Uri postTarget;
        var deposit = new Deposit
        {
            ArchivalGroup = archivalGroupRepositoryPath.HasText() ? new Uri(preservationHttpClient.BaseAddress!, archivalGroupRepositoryPath) : null,
            ArchivalGroupName = archivalGroupProposedName,
            SubmissionText = submissionText,
            Template = templateType
        };
        if (export)
        {
            postTarget = new Uri($"/{Deposit.BasePathElement}/export", UriKind.Relative);
            deposit.VersionExported = exportVersion;
        }
        else
        {
            postTarget = new Uri($"/{Deposit.BasePathElement}", UriKind.Relative);
        }
        try
        {
            HttpResponseMessage response = await preservationHttpClient.PostAsJsonAsync(postTarget, deposit, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var createdDeposit = await response.Content.ReadFromJsonAsync<Deposit>(cancellationToken: cancellationToken);
                if (createdDeposit is not null)
                {
                    return Result.Ok(createdDeposit);
                }
                return Result.Fail<Deposit>(ErrorCodes.UnknownError, "No deposit returned");
            }
            return await response.ToFailResult<Deposit>("Unable to create deposit");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    

    public async Task<Result<DepositQueryPage>> GetDeposits(DepositQuery? query, CancellationToken cancellationToken = default)
    {
        var responseStatusCode = -1;
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
            responseStatusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var deposits = await response.Content.ReadFromJsonAsync<DepositQueryPage>(cancellationToken: cancellationToken);
                if (deposits is not null)
                {
                    return Result.OkNotNull(deposits);
                }
                return Result.FailNotNull<DepositQueryPage>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<DepositQueryPage>("Unable to get Deposits");
        }
        catch (Exception e)
        {
            var errorCode = ErrorCodes.GetErrorCode(responseStatusCode);
            logger.LogError(e, "status code was {status}, error was {message}", responseStatusCode, e.Message);
            return Result.FailNotNull<DepositQueryPage>(errorCode, e.Message);
        }
    }

    public async Task<Result<List<ImportJobResult>>> GetImportJobResultsForDeposit(string depositId, CancellationToken cancellationToken)
    {        
        try
        {
            var uri = new Uri($"/deposits/{depositId}/importJobs/results", UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var jobResults = await response.Content.ReadFromJsonAsync<List<ImportJobResult>>(cancellationToken: cancellationToken);
                if (jobResults is not null)
                {
                    return Result.OkNotNull(jobResults);
                }
                return Result.FailNotNull<List<ImportJobResult>>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<List<ImportJobResult>>("Unable to get import job results");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<ImportJobResult>>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<ImportJobResult>> GetImportJobResult(string depositId, string importJobResultId, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri($"/deposits/{depositId}/importJobs/results/{importJobResultId}", UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var jobResult = await response.Content.ReadFromJsonAsync<ImportJobResult>(cancellationToken: cancellationToken);
                if (jobResult is not null)
                {
                    return Result.OkNotNull(jobResult);
                }
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<ImportJobResult>("Unable to get import job result");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<ImportJob>> GetDiffImportJob(string depositId, CancellationToken cancellationToken)
    { 
        try
        {
            var relPath = $"/deposits/{depositId}/importjobs/diff";
            var uri = new Uri(relPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var diffImportJob = await response.Content.ReadFromJsonAsync<ImportJob>(cancellationToken: cancellationToken);
                if (diffImportJob is not null)
                {
                    return Result.OkNotNull(diffImportJob);
                }
                return Result.FailNotNull<ImportJob>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<ImportJob>("Unable to get diff import job");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJob>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<ImportJobResult>> SendDiffImportJob(string depositId, CancellationToken cancellationToken)
    {
        try
        {
            var importJob = new ImportJob
            {
                Id = new Uri(preservationHttpClient.BaseAddress + $"/deposits/{depositId}/importjobs/diff")
            };
            var relPath = $"/deposits/{depositId}/importjobs";
            var uri = new Uri(relPath, UriKind.Relative);
            var response = await preservationHttpClient.PostAsJsonAsync(uri, importJob, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var diffImportJobResult = await response.Content.ReadFromJsonAsync<ImportJobResult>(cancellationToken: cancellationToken);
                if (diffImportJobResult is not null)
                {
                    return Result.OkNotNull(diffImportJobResult);
                }
                return Result.FailNotNull<ImportJobResult>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<ImportJobResult>("Unable to send diff import job");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
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
            if (response.IsSuccessStatusCode)
            {
                var deposit = await response.Content.ReadFromJsonAsync<Deposit>(cancellationToken: cancellationToken);
                if (deposit is not null)
                {
                    return Result.Ok(deposit);
                }
                return Result.Fail<Deposit>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailResult<Deposit>("Unable to get deposit");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<(string, string)>> GetMetsWithETag(string depositId, CancellationToken cancellationToken)
    {
        try
        {
            var relPath = $"/deposits/{depositId}/mets";
            var uri = new Uri(relPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var eTag = response.Headers.ETag!.Tag;
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.OkNotNull((content, eTag));
            }
            return await response.ToFailResult<(string, string)>("Unable to get mets with ETag");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<(string, string)>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<(Stream?, string?)> GetContentStream(string repositoryPath, CancellationToken cancellationToken)
    {
        var path = "/content/" + repositoryPath
            .RemoveStart("/")
            .RemoveStart(PreservedResource.BasePathElement)
            .RemoveStart("/");
        var uri = new Uri(path, UriKind.Relative);
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await preservationHttpClient.SendAsync(req, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadAsStreamAsync(cancellationToken), 
                response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
        }

        return (null, null);
    }

    public async Task<(Stream?, string?)> GetMetsStream(string archivalGrouprepositoryPath, string? version, CancellationToken cancellationToken)
    {
        var queryString = "?view=mets";
        if (version.HasText())
        {
            queryString += "&version=" + version;
        }
        var uri = new Uri(archivalGrouprepositoryPath + queryString, UriKind.Relative);
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await preservationHttpClient.SendAsync(req, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadAsStreamAsync(cancellationToken), 
                response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
        }

        return (null, null);
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