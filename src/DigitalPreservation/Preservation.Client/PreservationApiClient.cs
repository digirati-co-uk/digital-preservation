using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace Preservation.Client;

internal class PreservationApiClient(
    HttpClient httpClient,
    ILogger<PreservationApiClient> logger) : CommonApiBase(httpClient, logger), IPreservationApiClient
{
    private readonly HttpClient preservationHttpClient = httpClient;

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
            return await response.ToFailNotNullResult<List<Uri>>();
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
            return await response.ToFailResult<Deposit>();
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
            return await response.ToFailResult();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<Deposit?>> CreateDeposit(
        string? archivalGroupRepositoryPath,
        string? archivalGroupProposedName,
        string? submissionText,
        bool useObjectTemplate,
        bool export,
        string? exportVersion,
        CancellationToken cancellationToken = default)
    {
        Uri postTarget;
        var deposit = new Deposit
        {
            ArchivalGroup = archivalGroupRepositoryPath.HasText() ? new Uri(preservationHttpClient.BaseAddress!, archivalGroupRepositoryPath) : null,
            ArchivalGroupName = archivalGroupProposedName,
            SubmissionText = submissionText
        };
        if (export)
        {
            postTarget = new Uri($"/{Deposit.BasePathElement}/export", UriKind.Relative);
            deposit.VersionExported = exportVersion;
        }
        else
        {
            postTarget = new Uri($"/{Deposit.BasePathElement}", UriKind.Relative);
            deposit.UseObjectTemplate = useObjectTemplate;
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
            return await response.ToFailResult<Deposit>();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    

    public async Task<Result<DepositQueryPage>> GetDeposits(DepositQuery? query, CancellationToken cancellationToken = default)
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
            if (response.IsSuccessStatusCode)
            {
                var deposits = await response.Content.ReadFromJsonAsync<DepositQueryPage>(cancellationToken: cancellationToken);
                if (deposits is not null)
                {
                    return Result.OkNotNull(deposits);
                }
                return Result.FailNotNull<DepositQueryPage>(ErrorCodes.NotFound, "No resource at " + uri);
            }
            return await response.ToFailNotNullResult<DepositQueryPage>();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<DepositQueryPage>(ErrorCodes.UnknownError, e.Message);
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
            return await response.ToFailNotNullResult<List<ImportJobResult>>();
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
            return await response.ToFailNotNullResult<ImportJobResult>();
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
            return await response.ToFailNotNullResult<ImportJob>();
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
            return await response.ToFailNotNullResult<ImportJobResult>();
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
            return await response.ToFailResult<Deposit>();
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