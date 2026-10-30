using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using LeedsDlipServices.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;
using System.Net.Http.Json;

namespace Preservation.Client;

public class PreservationApiClient(
    HttpClient httpClient,
    IMemoryCache memoryCache,
    ILogger<PreservationApiClient> logger) : CommonApiBase(httpClient, logger), IPreservationApiClient
{
    private readonly HttpClient preservationHttpClient = httpClient;


    public async Task<Result<SearchCollection?>> Search(string text, int? page = 0, int? pageSize = 50, SearchType type = SearchType.All, int otherPage = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = new Uri($"/search?text={Uri.EscapeDataString(text)}&pageNumber={page}&pageSize={pageSize}&type={type}&otherPage={otherPage}", UriKind.Relative);
            var response = await preservationHttpClient.GetFromJsonAsync<SearchCollection>(uri, cancellationToken: cancellationToken);
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
        return await SendForResult(
            () => preservationHttpClient.PostAsync(uri, null, cancellationToken),
            "Unable to lock deposit",
            "Could not lock deposit");
    }

    public async Task<Result> ReleaseDepositLock(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath + "/lock", UriKind.Relative);
        return await SendForResult(
            () => preservationHttpClient.DeleteAsync(uri, cancellationToken),
            "Unable to remove lock",
            "Could not release deposit lock");
    }

    public async Task<Result> RunPipeline(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath + "/pipeline", UriKind.Relative);
        return await SendForResult(
            () => preservationHttpClient.PostAsync(uri, null, cancellationToken),
            "Unable to run pipeline",
            "Could not run pipeline");
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
        var uri = new Uri("/agents", UriKind.Relative);
        return await SendForJsonNotNull<List<Uri>>(
            () => preservationHttpClient.GetAsync(uri, cancellationToken),
            "Unable to get agents",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get all agents",
            cancellationToken: cancellationToken);
    }


    public async Task<Result<Deposit?>> UpdateDeposit(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath, UriKind.Relative);
        return await SendForJson<Deposit>(
            () => preservationHttpClient.PatchAsJsonAsync(uri, deposit, cancellationToken),
            "Unable to update deposit",
            ErrorCodes.UnknownError,
            "No deposit returned",
            "Could not update deposit",
            cancellationToken: cancellationToken);
    }

    public async Task<Result> DeleteDeposit(string id, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{id}", UriKind.Relative);
        return await SendForResult(
            () => preservationHttpClient.DeleteAsync(uri, cancellationToken),
            "Unable to delete deposit",
            "Could not delete deposit");
    }

    public async Task<Result<Deposit?>> CreateDepositFromIdentifier(string schema, string identifier, TemplateType templateType, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/{Deposit.BasePathElement}/from-identifier", UriKind.Relative);
        var body = new SchemaAndValue{ Schema = schema, Value = identifier, Template = templateType };
        return await SendForJson<Deposit>(
            () => preservationHttpClient.PostAsJsonAsync(uri, body, cancellationToken),
            "Unable to create deposit from identifier",
            ErrorCodes.UnknownError,
            "No deposit returned",
            "Could not create deposit",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<Deposit?>> CreateDeposit(
        string? archivalGroupRepositoryPath,
        string? archivalGroupProposedName,
        string? submissionText,
        TemplateType templateType,
        bool export,
        string? exportVersion,
        CancellationToken cancellationToken)
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
        return await SendForJson<Deposit>(
            () => preservationHttpClient.PostAsJsonAsync(postTarget, deposit, cancellationToken),
            "Unable to create deposit",
            ErrorCodes.UnknownError,
            "No deposit returned",
            "Could not create deposit",
            cancellationToken: cancellationToken);
    }



    public async Task<Result<DepositQueryPage>> GetDeposits(DepositQuery? query, CancellationToken cancellationToken = default)
    {
        var relPath = "/deposits";
        var queryString = QueryBuilder.MakeQueryString(query);
        if (queryString.HasText())
        {
            relPath += $"?{queryString}";
        }
        var uri = new Uri(relPath, UriKind.Relative);
        // The previous hand-rolled version caught exceptions and re-derived an ErrorCode from the
        // last-seen response status code, rather than always reporting UnknownError - but that
        // only ever runs after IsSuccessStatusCode was already true (the exception came from
        // parsing a successful response), and GetErrorCode has no case for a 2xx status, so it
        // always fell through to UnknownError anyway. Nothing behavioural is lost here.
        return await SendForJsonNotNull<DepositQueryPage>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get Deposits",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            cancellationToken: cancellationToken);
    }

    public async Task<Result<List<ImportJobResult>>> GetImportJobResultsForDeposit(string depositId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{depositId}/importJobs/results", UriKind.Relative);
        return await SendForJsonNotNull<List<ImportJobResult>>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get import job results",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get import job results for deposit",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<ImportJobResult>> GetImportJobResult(string depositId, string importJobResultId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{depositId}/importJobs/results/{importJobResultId}", UriKind.Relative);
        return await SendForJsonNotNull<ImportJobResult>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get import job result",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get import job result",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<ImportJob>> GetDiffImportJob(string depositId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{depositId}/importjobs/diff", UriKind.Relative);
        return await SendForJsonNotNull<ImportJob>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get diff import job",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get diff import job",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<ImportJobResult>> SendDiffImportJob(string depositId, bool suppressActivityStreamEvent, CancellationToken cancellationToken)
    {
        var importJob = new ImportJob
        {
            Id = new Uri(preservationHttpClient.BaseAddress + $"/deposits/{depositId}/importjobs/diff"),
            SuppressActivityStreamEvent = suppressActivityStreamEvent
        };
        var uri = new Uri($"/deposits/{depositId}/importjobs", UriKind.Relative);
        return await SendForJsonNotNull<ImportJobResult>(
            () => preservationHttpClient.PostAsJsonAsync(uri, importJob, cancellationToken),
            "Unable to send diff import job",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not send diff import job",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<MetsIdNormalisationReport>> NormaliseDepositMetsIds(
        string depositId, string? metsETag, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{depositId}/mets/normalise", UriKind.Relative);
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (metsETag.HasText())
        {
            // The API enforces If-Match only when it is sent, so sending it is what makes the
            // documented 409-on-concurrent-edit real rather than dead code.
            request.Headers.TryAddWithoutValidation("If-Match", metsETag);
        }
        return await SendForJsonNotNull<MetsIdNormalisationReport>(
            () => preservationHttpClient.SendAsync(request, cancellationToken),
            "Unable to normalise METS IDs",
            ErrorCodes.UnknownError,
            "No normalisation report returned by " + uri,
            $"Could not normalise METS IDs for deposit {depositId}",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<Deposit?>> GetDeposit(string id, CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"/deposits/{id}", UriKind.Relative);
        var result = await SendForJson<Deposit>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get deposit",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get deposit",
            cancellationToken: cancellationToken);
        if (result is { Success: true, Value: not null })
        {
            logger.LogInformation("In preservation API client for deposit {Deposit} Lock date: {LockDate}, Locked by: {LockedBy} ",
                result.Value.Id, result.Value.LockDate, result.Value.LockedBy);
        }
        return result;
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
            logger.LogError(e, "Could not get METS with eTag");
            return Result.Fail<(string, string)>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<string>> GetParsedDepositMets(string depositId, CancellationToken cancellationToken)
    {
        var relPath = $"/deposits/{depositId}/parsed-mets";
        return await ProxyStringFromPreservationApi(cancellationToken, relPath);
    }    
    
    public async Task<Result<string>> GetIIIF(string depositId, CancellationToken cancellationToken)
    {
        var relPath = $"/deposits/{depositId}/iiif";
        return await ProxyStringFromPreservationApi(cancellationToken, relPath);
    }

    private async Task<Result<string>> ProxyStringFromPreservationApi(CancellationToken cancellationToken, string relPath)
    {
        try
        {
            var uri = new Uri(relPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await preservationHttpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.OkNotNull(content);
            }
            return await response.ToFailNotNullResult<string>("Unable to GET " + relPath);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not GET {RelPath}", relPath);
            return Result.FailNotNull<string>(ErrorCodes.UnknownError, e.Message);
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

    public async Task<(Stream?, string?)> GetMetsStream(string archivalGroupPathUnderRoot, bool parsedJson = false, CancellationToken cancellationToken = default)
    {
        var queryString = "?view=" + (parsedJson ? "parsed-mets" : "mets");
        var uri = new Uri(archivalGroupPathUnderRoot + queryString, UriKind.Relative);
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
        catch (Exception e)
        {
            logger.LogError(e, "Error occured while checking if Fedora is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd,
                Success = false,
                Error = e.Message
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
        catch (Exception e)
        {
            logger.LogError(e, "Error occured while checking if Preservation API can see S3");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.PreservationApiReadS3,
                Success = false,
                Error = e.Message
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
        catch (Exception e)
        {
            logger.LogError(e, "Error occured while checking if Preservation API can see that Storage API can see S3");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.StorageApiReadS3,
                Success = false,
                Error = e.Message
            };
        }
    }


    public async Task<Result<List<ProcessPipelineResult>>> GetPipelineJobResultsForDeposit(string depositId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{depositId}/pipelinerunjobs", UriKind.Relative);
        return await SendForJsonNotNull<List<ProcessPipelineResult>>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get pipeline run job results",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get get pipeline job results for deposit",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<ProcessPipelineResult>> GetPipelineJobResult(string depositId, string pipelineJobResultId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/deposits/{depositId}/pipelinerunjobs/{pipelineJobResultId}", UriKind.Relative);
        return await SendForJsonNotNull<ProcessPipelineResult>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get pipeline run job result",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Could not get pipeline job result",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<LogPipelineStatusResult>> LogPipelineRunStatus([FromBody] PipelineDeposit pipelineDeposit, CancellationToken cancellationToken)
    {
        // The success value isn't read from the response body - it's synthesised locally - so this
        // is SendForResult (success/fail only), not SendForJsonNotNull, with the success value built
        // from the already-known request rather than parsed from anything the API returned.
        var uri = new Uri("/Deposits/pipeline-status", UriKind.Relative);
        var result = await SendForResult(
            () => preservationHttpClient.PostAsJsonAsync(uri, pipelineDeposit, cancellationToken),
            "Unable to update pipeline status.",
            "Error calling pipeline status endpoint");
        return result.Success
            ? Result.OkNotNull(new LogPipelineStatusResult { Status = "Success", JobId = pipelineDeposit.Id })
            : Result.FailNotNull<LogPipelineStatusResult>(result.ErrorCode!, result.ErrorMessage);
    }

    public async Task<Result> ActivateDeposit(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath + "/activate", UriKind.Relative);
        return await SendForResult(
            () => preservationHttpClient.PutAsync(uri, null, cancellationToken),
            "Unable to Activate deposit",
            "Could not activate deposit");
    }

    public async Task<Result> DeactivateDeposit(Deposit deposit, CancellationToken cancellationToken)
    {
        var uri = new Uri(deposit.Id!.AbsolutePath + "/deactivate", UriKind.Relative);
        return await SendForResult(
            () => preservationHttpClient.PutAsync(uri, null, cancellationToken),
            "Unable to Deactivate deposit",
            "Could not deactivate deposit");
    }

    public async Task<Result<ArchiveJobResult>> GetArchiveJobResult(string depositId, CancellationToken cancellationToken)
    {
        var uri = new Uri($"/DepositArchiveJobs/{depositId}", UriKind.Relative);
        return await SendForJsonNotNull<ArchiveJobResult>(
            () => preservationHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken),
            "Unable to get archive run job result",
            ErrorCodes.NotFound,
            "No resource at " + uri,
            "Error getting archive job result",
            cancellationToken: cancellationToken);
    }

    public async Task<Result<ArchiveJobResult>> ArchiveDeposit([FromBody] ArchiveDepositJob archiveDepositJob, CancellationToken cancellationToken)
    {
        // As with LogPipelineRunStatus: the success value is synthesised, not parsed from the
        // response, so this composes SendForResult rather than SendForJsonNotNull.
        var uri = new Uri("/Deposits/archive-job", UriKind.Relative);
        var result = await SendForResult(
            () => preservationHttpClient.PostAsJsonAsync(uri, archiveDepositJob, cancellationToken),
            "Unable to archive deposit.",
            "Error archiving deposit");
        return result.Success
            ? Result.OkNotNull(new ArchiveJobResult { Status = "Success", DepositId = archiveDepositJob.DepositId })
            : Result.FailNotNull<ArchiveJobResult>(result.ErrorCode!, result.ErrorMessage);
    }

    // GetAccessConditions and GetRangeTypes are deliberately NOT migrated to SendForJsonNotNull
    // (issue #388 item 2.2): both treat a null/absent body on success as an empty list via `?? []`,
    // not as a failure - the one place in this file an empty body isn't itself an error - and the
    // shared helper has no equivalent of that fallback.
    public async Task<Result<AccessConditionProfile>> GetAccessConditions(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "preservation:access-conditions";
        if (memoryCache.TryGetValue(cacheKey, out AccessConditionProfile? cached))
            return Result.OkNotNull(cached!);
        try
        {
            var response = await preservationHttpClient.GetAsync("/access-conditions", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var profile = await response.Content.ReadFromJsonAsync<AccessConditionProfile>(
                    cancellationToken: cancellationToken) ?? new AccessConditionProfile();
                // Short enough that a redeployed API profile reaches the UI without a UI restart
                // (issue #433), long enough that the METS writer's per-write fetch is nearly free.
                memoryCache.Set(cacheKey, profile, TimeSpan.FromMinutes(5));
                return Result.OkNotNull(profile);
            }
            return await response.ToFailNotNullResult<AccessConditionProfile>("Unable to get access conditions");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get access conditions");
            return Result.FailNotNull<AccessConditionProfile>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<List<string>>> GetRangeTypes(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "preservation:range-types";
        if (memoryCache.TryGetValue(cacheKey, out List<string>? cached))
            return Result.OkNotNull(cached!);
        try
        {
            var response = await preservationHttpClient.GetAsync("/range-types", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var rangeTypes = await response.Content.ReadFromJsonAsync<List<string>>(
                    cancellationToken: cancellationToken) ?? [];
                memoryCache.Set(cacheKey, rangeTypes, TimeSpan.FromHours(1));
                return Result.OkNotNull(rangeTypes);
            }
            return await response.ToFailNotNullResult<List<string>>("Unable to get range types");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get range types");
            return Result.FailNotNull<List<string>>(ErrorCodes.UnknownError, e.Message);
        }
    }

}