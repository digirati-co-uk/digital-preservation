using System.Security.Claims;
using System.Text.Json;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Utils;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using ImportJobEntity = Preservation.API.Data.Entities.ImportJob;

namespace Preservation.API.Features.ImportJobs.Requests;

public class ExecuteImportJob(ImportJob importJob, ClaimsPrincipal principal) : IRequest<Result<ImportJobResult>>
{
    public ImportJob ImportJob { get; } = importJob;
    public ClaimsPrincipal Principal { get; } = principal;
}

public class ExecuteImportJobHandler(
    ILogger<ExecuteImportJobHandler> logger,
    IStorageApiClient storageApi,
    PreservationContext dbContext,
    IIdentityService identityService,
    ResourceMutator resourceMutator) : IRequestHandler<ExecuteImportJob, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(ExecuteImportJob request, CancellationToken cancellationToken)
    {
        var callerIdentity = request.Principal.GetCallerIdentity();
        logger.LogInformation("ExecuteImportJobHandler handling import job " + request.ImportJob.LogSummary());
        var now = DateTime.UtcNow;
        var mintedId = identityService.MintIdentity(nameof(ImportJob));
        logger.LogInformation("Identity service gave us id for import job: " + mintedId);
        
        // Overwrite this, regardless of what the incoming request says
        request.ImportJob.LastModifiedBy = resourceMutator.GetAgentUri(callerIdentity);
        request.ImportJob.CreatedBy ??= request.ImportJob.LastModifiedBy;
        request.ImportJob.LastModified ??= DateTime.UtcNow;
        request.ImportJob.Created ??= request.ImportJob.LastModified;
        
        var storageApiImportJob = Duplicate(request.ImportJob);
        logger.LogInformation("Mutating Preservation Import Job");
        resourceMutator.MutatePreservationImportJob(storageApiImportJob);
        var storageImportJobResultResult = await storageApi.ExecuteImportJob(storageApiImportJob, cancellationToken);
        if (storageImportJobResultResult is { Success: true, Value: not null })
        {
            logger.LogInformation("Storage API accepted import job");
            var storageImportJobResult = storageImportJobResultResult.Value;
            logger.LogInformation("Storage API returned Import Job Result " + storageImportJobResult.LogSummary());
            var preservationImportJobResult = Duplicate(storageImportJobResult);
            resourceMutator.MutateStorageImportJobResult(preservationImportJobResult, request.ImportJob.Deposit!, mintedId);
            preservationImportJobResult.OriginalImportJob = request.ImportJob.OriginalId;
            logger.LogInformation("Mutated into Preservation API Import Job Result " + preservationImportJobResult.LogSummary());
            var entity = new ImportJobEntity
            {
                StorageImportJobResultId = storageImportJobResult.Id!,
                Id = mintedId,
                ArchivalGroup = request.ImportJob.ArchivalGroup!,
                ImportJobJson = JsonSerializer.Serialize(request.ImportJob),
                Status = storageImportJobResult.Status,
                Deposit = request.ImportJob.Deposit!.GetSlug()!,
                LastUpdated = now,
                DateSubmitted = now,
                SourceVersion = storageImportJobResult.SourceVersion,
                LatestStorageApiResultJson = JsonSerializer.Serialize(storageImportJobResult),
                LatestPreservationApiResultJson = JsonSerializer.Serialize(preservationImportJobResult)
            };
            dbContext.ImportJobs.Add(entity);
            logger.LogInformation("Saving Import Job entity " + entity.Id + " to DB");
            await dbContext.SaveChangesAsync(cancellationToken);
            
            return Result.OkNotNull(preservationImportJobResult);
        }

        logger.LogWarning("Import job did not execute: " + storageImportJobResultResult.CodeAndMessage());
        return storageImportJobResultResult;
    }

    // sorry
    private static ImportJob Duplicate(ImportJob importJob)
    {
        var serialized = JsonSerializer.Serialize(importJob);
        return JsonSerializer.Deserialize<ImportJob>(serialized)!;
    }
    private static ImportJobResult Duplicate(ImportJobResult importJobResult)
    {
        var serialized = JsonSerializer.Serialize(importJobResult);
        return JsonSerializer.Deserialize<ImportJobResult>(serialized)!;
    }
}