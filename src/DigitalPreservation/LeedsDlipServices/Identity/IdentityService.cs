using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;

namespace LeedsDlipServices.Identity;

public class IdentityService(
    HttpClient httpClient,
    IOptions<IdentityOptions> options) : IIdentityService
{
    const string ApiPrefix = "/api/v1/";
    private readonly IdentityOptions identityOptions = options.Value;
    
    public string MintIdentity(string resourceType, Uri? equivalent = null)
    {
        return Identifiable.Generate(12, true);
    }

    private async Task<Result<IdentityRecord>> GetSingleIdentityFromSchemaQuery(
        string schema, string q, CancellationToken cancellationToken)
    {
        if (schema == "id" || schema == "pid")
        {
            return await GetIdentityDirect(q, cancellationToken);
        }
        try
        {
            var uri = new Uri($"{ApiPrefix}ids?q={q}&s={schema}", UriKind.Relative);
            var response = await httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var queryResult = await response.Content.ReadFromJsonAsync<QueryResult>(cancellationToken: cancellationToken);
                if (queryResult == null)
                {
                    return Result.FailNotNull<IdentityRecord>(ErrorCodes.UnknownError, "Unable to deserialize IdentityRecord response");
                }
                if (queryResult.Results == null || queryResult.Results.Count == 0)
                {
                    return Result.FailNotNull<IdentityRecord>(ErrorCodes.NotFound, $"No Identity record found for {schema}={q}");
                }
                if (queryResult.Results.Count > 1)
                {
                    return Result.FailNotNull<IdentityRecord>(ErrorCodes.UnknownError, $"Multiple results ({queryResult.Results.Count}) found for {schema}={q}");
                }
                var identityRecord = queryResult.Results[0];
                var mutated = Mutate(identityRecord);
                return Result.OkNotNull(mutated);
                
            }

            var errorCode = ErrorCodes.GetErrorCode((int?)response.StatusCode);
            return Result.FailNotNull<IdentityRecord>(errorCode, 
                $"Identity Service returned {response.StatusCode} for {schema}={q}, {response.ReasonPhrase ?? "(no reason given)"}");
        }
        catch (Exception e)
        {
            return Result.FailNotNull<IdentityRecord>(ErrorCodes.UnknownError, e.Message);
        }
    }

    private async Task<Result<IdentityRecord>> GetIdentityDirect(string pid, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri($"{ApiPrefix}ids/{pid}", UriKind.Relative);
            var response = await httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var identityRecord = await response.Content.ReadFromJsonAsync<IdentityRecord>(cancellationToken: cancellationToken);
                if (identityRecord == null)
                {
                    return Result.FailNotNull<IdentityRecord>(ErrorCodes.NotFound, "Unable to find or deserialize IdentityRecord response");
                }
                var mutated = Mutate(identityRecord);
                return Result.OkNotNull(mutated);
                
            }

            var errorCode = ErrorCodes.GetErrorCode((int?)response.StatusCode);
            return Result.FailNotNull<IdentityRecord>(errorCode, 
                $"Identity Service returned {response.StatusCode} for id={pid}, {response.ReasonPhrase ?? "(no reason given)"}");
        }
        catch (Exception e)
        {
            return Result.FailNotNull<IdentityRecord>(ErrorCodes.UnknownError, e.Message);
        }
    }

    private IdentityRecord Mutate(IdentityRecord identityFromService)
    {
        var mutated = identityFromService.Clone();
        var repositoryPath = identityFromService.RepositoryUri!.AbsolutePath;
        if (identityOptions.AlternativeCollectionsContainer.HasText())
        {
            repositoryPath =
                repositoryPath.ReplaceFirst("/cc/", $"/{identityOptions.AlternativeCollectionsContainer}/");
        }
        mutated.RepositoryUri = new Uri(identityOptions.PreservationRoot, repositoryPath);
        return mutated;
    }

    public async Task<Result<IdentityRecord>> GetIdentityBySchema(SchemaAndValue schemaAndValue, CancellationToken cancellationToken)
    {
        var result = await GetSingleIdentityFromSchemaQuery(schemaAndValue.Schema, schemaAndValue.Value, cancellationToken);
        return result;
    }


    public async Task<Result<IdentityRecord>> GetIdentityByCatIrn(string catIrn, CancellationToken cancellationToken)
    {
        var result = await GetSingleIdentityFromSchemaQuery("catirn", catIrn, cancellationToken);
        return result;
    }

    public async Task<Result<IdentityRecord>> GetIdentityByArchivalGroup(Uri archivalGroupUri, CancellationToken cancellationToken)
    {
        var result = await GetSingleIdentityFromSchemaQuery("repositoryuri", archivalGroupUri.ToString(), cancellationToken);
        return result;
    }
}