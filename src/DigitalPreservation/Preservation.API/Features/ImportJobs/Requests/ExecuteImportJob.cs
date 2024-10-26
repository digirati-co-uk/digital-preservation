using System.Text.Json;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using ImportJobEntity = Preservation.API.Data.Entities.ImportJob;

namespace Preservation.API.Features.ImportJobs.Requests;

public class ExecuteImportJob(ImportJob importJob) : IRequest<Result<ImportJobResult>>
{
    public ImportJob ImportJob { get; } = importJob;
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
        var now = DateTime.UtcNow;
        var mintedId = identityService.MintIdentity(nameof(ImportJob));
        var storageApiImportJob = Duplicate(request.ImportJob);
        resourceMutator.MutatePreservationImportJob(storageApiImportJob);
        var storageImportJobResultResult = await storageApi.ExecuteImportJob(storageApiImportJob, cancellationToken);
        if (storageImportJobResultResult is { Success: true, Value: not null })
        {
            var storageImportJobResult = storageImportJobResultResult.Value;
            var entity = new ImportJobEntity
            {
                StorageImportJobResultId = storageImportJobResult.Id!,
                Id = mintedId,
                ArchivalGroup = request.ImportJob.ArchivalGroup,
                ImportJobJson = JsonSerializer.Serialize(request.ImportJob),
                Status = storageImportJobResult.Status,
                Deposit = request.ImportJob.Deposit!,
                LastUpdated = now,
                DateSubmitted = now,
                SourceVersion = storageImportJobResult.SourceVersion,
                LatestStorageApiResultJson = JsonSerializer.Serialize(storageImportJobResult)
            };
            dbContext.ImportJobs.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            var preservationImportJobResult = storageImportJobResult;
            // we don't need to use this again here but this is not the right way to do it;
            // this mutator should return a new object?
            resourceMutator.MutateStorageImportJobResult(preservationImportJobResult, request.ImportJob.Deposit!, mintedId);
            return Result.OkNotNull(preservationImportJobResult);
        }

        return storageImportJobResultResult;
    }

    // sorry
    private static ImportJob Duplicate(ImportJob importJob)
    {
        var serialized = JsonSerializer.Serialize(importJob);
        return JsonSerializer.Deserialize<ImportJob>(serialized)!;
    }
}