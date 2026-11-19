using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;

using HttpHeaders = DigitalPreservation.Core.Web.Headers.HttpHeaders;

namespace DigitalPreservation.CommonApiClient;

/// <summary>
/// This class contains client code that would be identical in Storage API and Preservation API clients
/// </summary>
/// <param name="httpClient"></param>
/// <param name="logger"></param>
public abstract class CommonApiBase(HttpClient httpClient, ILogger logger)
{
    /// <summary>
    /// Sends a request and parses a JSON body that must be present on success - the shape most of
    /// StorageApiClient and PreservationApiClient wrote out by hand, about 19 times between them
    /// (issue #388 item 2.2): send, check <see cref="HttpResponseMessage.IsSuccessStatusCode"/>,
    /// parse, treat an unexpectedly empty body as its own failure, convert a non-success response
    /// via <see cref="ProblemDetailsX.ToFailNotNullResult{T}(HttpResponseMessage, string)"/>, and
    /// catch/log/report anything thrown as <see cref="ErrorCodes.UnknownError"/>. <paramref name="send"/>
    /// is a delegate rather than a built request so callers keep using whichever HttpClient
    /// convenience method already shapes their call (GetAsync, PostAsJsonAsync, PatchAsJsonAsync...).
    /// </summary>
    protected async Task<Result<T>> SendForJsonNotNull<T>(
        Func<Task<HttpResponseMessage>> send,
        string failureMessage,
        string emptyBodyErrorCode,
        string emptyBodyMessage,
        string? logMessage = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await send();
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
                if (value is not null)
                {
                    return Result.OkNotNull(value);
                }
                return Result.FailNotNull<T>(emptyBodyErrorCode, emptyBodyMessage);
            }
            return await response.ToFailNotNullResult<T>(failureMessage);
        }
        catch (Exception e)
        {
            logger.LogError(e, logMessage ?? e.Message);
            return Result.FailNotNull<T>(ErrorCodes.UnknownError, e.Message);
        }
    }

    /// <summary>
    /// The null-tolerant twin of <see cref="SendForJsonNotNull{T}"/>, for the call sites that return
    /// <c>Result&lt;T?&gt;</c> (<see cref="Result.Ok{T}"/>/<see cref="Result.Fail{T}"/>) rather than
    /// the never-null <c>Result&lt;T&gt;</c> shape. Otherwise identical.
    /// </summary>
    protected async Task<Result<T?>> SendForJson<T>(
        Func<Task<HttpResponseMessage>> send,
        string failureMessage,
        string emptyBodyErrorCode,
        string emptyBodyMessage,
        string? logMessage = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await send();
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
                if (value is not null)
                {
                    return Result.Ok<T>(value);
                }
                return Result.Fail<T>(emptyBodyErrorCode, emptyBodyMessage);
            }
            return await response.ToFailResult<T>(failureMessage);
        }
        catch (Exception e)
        {
            logger.LogError(e, logMessage ?? e.Message);
            return Result.Fail<T>(ErrorCodes.UnknownError, e.Message);
        }
    }

    /// <summary>
    /// Sends a request where only success or failure matters, no response body - the no-content
    /// twin of <see cref="SendForJsonNotNull{T}"/>. PreservationApiClient's LockDeposit and its
    /// siblings wrote this out with no try/catch at all (issue #388 item 2.2's "catches nothing"
    /// divergence); this closes that gap as a side effect of removing the duplication, since an
    /// uncaught exception here is a bug regardless of how many copies of the code it appears in.
    /// </summary>
    protected async Task<Result> SendForResult(
        Func<Task<HttpResponseMessage>> send,
        string failureMessage,
        string? logMessage = null)
    {
        try
        {
            var response = await send();
            if (response.IsSuccessStatusCode)
            {
                return Result.Ok();
            }
            return await response.ToFailResult(failureMessage);
        }
        catch (Exception e)
        {
            logger.LogError(e, logMessage ?? e.Message);
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<PreservedResource?>> GetResource(string path)
    {
        return await GetResourceInternal(path);
    }

    public async Task<Result<ArchivalGroup?>> GetArchivalGroup(string path)
    {
        logger.LogInformation("Getting ArchivalGroup {Path}", path);
        var result = await GetResourceInternal(path);
        if (result.Success)
        {
            return Result.Ok<ArchivalGroup?>(result.Value as ArchivalGroup);
        }
        logger.LogWarning("Failed to get ArchivalGroup {Path}: {CodeAndMessage}", path, result.CodeAndMessage());
        return Result.Cast<PreservedResource?, ArchivalGroup?>(result);
    }

    
    private async Task<Result<PreservedResource?>> GetResourceInternal(string path)
    {        
        // path MUST be the full /repository... path, which we just pass through as-is
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);

            var response = await httpClient.SendAsync(req);
            var stream = await response.Content.ReadAsStreamAsync();
            if (response.IsSuccessStatusCode)
            {
                var parsed = PreservedResourceDeserializer.Parse(stream);
                if (parsed != null)
                {
                    return Result.Ok<PreservedResource?>(parsed);
                }
                return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<PreservedResource>("Unable to GET Resource");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result<PreservedResource?>> GetLightweightResource(string pathUnderRoot, string? version)
    {        
        var reqPath = $"{PreservedResource.BasePathElement}/{pathUnderRoot}?view=lightweight";
        if (version.HasText())
        {
            reqPath = $"{reqPath}&version={version}";
        }
        try
        {
            var uri = new Uri(reqPath, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await httpClient.SendAsync(req);
            var stream = await response.Content.ReadAsStreamAsync();
            if (response.IsSuccessStatusCode)
            {
                var parsed = PreservedResourceDeserializer.Parse(stream);
                if (parsed != null)
                {
                    return Result.Ok<PreservedResource?>(parsed);
                }
                return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<PreservedResource>("Unable to GET Resource");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<PreservedResource?>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<string?>> GetResourceType(string path)
    {     
        // path MUST be the full /repository... path, which we just pass through as-is
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            var req = new HttpRequestMessage(HttpMethod.Head, uri);
            var response = await httpClient.SendAsync(req);
            if (response.IsSuccessStatusCode && response.Headers.TryGetValues(HttpHeaders.XPreservationResourceType, out var values))
            {
                var resourceType = values.FirstOrDefault();
                if (resourceType != null)
                {
                    return Result.Ok(resourceType);
                }
            }

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                return Result.Fail<string?>(ErrorCodes.Tombstone, "Resource has been replaced by a Tombstone.");
            }
            var errorCode = ErrorCodes.GetErrorCode((int?)response.StatusCode);
            return Result.Fail<string?>(errorCode, "Resource Type could not be retrieved.");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<string?>(ErrorCodes.UnknownError, e.Message);
        }
        
    }

    
    
    public async Task<Result<Container?>> CreateContainer(string path, string? name = null)
    {
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            HttpResponseMessage response;
            if (name.HasText())
            {
                var container = new Container { Name = name };
                response = await httpClient.PutAsJsonAsync(uri, container);
            }
            else
            {
                response = await httpClient.PutAsync(uri, null);
            }
            var stream = await response.Content.ReadAsStreamAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var parsed = PreservedResourceDeserializer.Parse(stream);
                if (parsed is Container createdContainer)
                {
                    return Result.Ok<Container?>(createdContainer);
                }
                return Result.Fail<Container?>(ErrorCodes.UnknownError, "Resource could not be parsed.");
            }
            return await response.ToFailResult<Container>("Unable to create Container");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Container?>(ErrorCodes.UnknownError, e.Message);
        }
    }
    
    public async Task<Result> DeleteContainer(string path, bool purge, CancellationToken cancellationToken)
    {
        // Make this work the same way that Fedora does; surface the tombstone through the two APIs
        // https://wiki.lyrasis.org/display/FEDORA6x/Delete+vs+Purge
        var uri = new Uri($"{path}?purge={purge}", UriKind.Relative);
        return await SendForResult(
            () => httpClient.DeleteAsync(uri, cancellationToken),
            "Unable to delete Container");
    }

    public async Task<Result<ArchivalGroup?>> TestArchivalGroupPathInternal(string reqPath)
    {
        var uri = new Uri(reqPath, UriKind.Relative);
        return await SendForJson<ArchivalGroup>(
            () => httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri)),
            "Unable to test archival group path",
            ErrorCodes.UnknownError,
            "Resource could not be parsed.");
    }

    public async Task<Result<StorageMap>> GetStorageMap(string archivalGroupPathUnderRoot, string? version = null)
    {
        var reqPath = $"ocfl/storagemap/{archivalGroupPathUnderRoot}";
        if (version.HasText())
        {
            reqPath = $"{reqPath}?version={version}";
        }
        // issue #388 item 2.2: this used to skip the IsSuccessStatusCode check entirely, so a
        // failure response (e.g. 404) was parsed as a StorageMap, failed, and was reported as a
        // generic "Resource could not be parsed"/UnknownError regardless of the real cause.
        // SendForJsonNotNull's status check fixes that - a failure now carries the real error code.
        var result = await SendForJsonNotNull<StorageMap>(
            () => httpClient.GetAsync(reqPath),
            "Unable to get storage map",
            ErrorCodes.UnknownError,
            "Resource could not be parsed.");
        if (result.Success)
        {
            logger.LogInformation("Received storageMap for ArchivalGroup {ArchivalGroupPathUnderRoot}, version {Version}",
                archivalGroupPathUnderRoot, version);
        }
        return result;
    }
}