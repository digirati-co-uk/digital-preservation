using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetImportJobResult(string depositId, string importJobId) : IRequest<Result<ImportJobResult>>
{
    public string DepositId { get; } = depositId;
    public string ImportJobId { get; } = importJobId;
}

public class GetImportJobResultHandler(
    ILogger<GetImportJobResultHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApi,
    ResourceMutator resourceMutator,
    IStorage storage) : IRequestHandler<GetImportJobResult, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(GetImportJobResult request, CancellationToken cancellationToken)
    {
        // get the preservation one out of the DB by id
        var entity = dbContext.ImportJobs.SingleOrDefault(j => j.Id == request.ImportJobId && j.Deposit == request.DepositId);
        if (entity == null)
        {
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.NotFound, "No import job found");
        }

        if (ImportJobStates.IsComplete(entity.Status))
        {
            if (entity.LatestPreservationApiResultJson.HasText())
            {
                var jobResult = JsonSerializer.Deserialize<ImportJobResult>(entity.LatestPreservationApiResultJson!);
                if (jobResult != null)
                {
                    return Result.OkNotNull(jobResult);
                }
            }
            // It's marked complete but there is no JSON
            // Is this ever a valid place or is it an error?
            // We could construct an ImportJobResult from entity
            // TODO: Fail for now 
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "Job Complete but no LatestPreservationApiResultJson");
        }
        
        // It's not complete: ask the storage API for its version, get its status
        var importJobResultResult = await storageApi.GetImportJobResult(entity.StorageImportJobResultId);
        if (importJobResultResult.Success)
        {
            var storageApiImportJobResult = importJobResultResult.Value!;
            var preservationApiImportJobResult = Duplicate(storageApiImportJobResult);
            resourceMutator.MutateStorageImportJobResult(preservationApiImportJobResult, entity.Deposit, entity.Id);
            
            // update the entity
            bool wasComplete = ImportJobStates.IsComplete(entity.Status);
            entity.Status = storageApiImportJobResult.Status;
            bool isComplete = ImportJobStates.IsComplete(entity.Status);
            // If status is a change to Completed we could do something more
            if (storageApiImportJobResult.Errors != null && storageApiImportJobResult.Errors.Length != 0)
            {
                entity.Errors = string.Join("; ", storageApiImportJobResult.Errors.Select(e => e.Message));
            }
            entity.DateBegun = storageApiImportJobResult.DateBegun;
            entity.DateFinished = storageApiImportJobResult.DateFinished;
            entity.NewVersion = storageApiImportJobResult.NewVersion;
            entity.SourceVersion = storageApiImportJobResult.SourceVersion;
            entity.LatestStorageApiResultJson = JsonSerializer.Serialize(storageApiImportJobResult);
            entity.LatestPreservationApiResultJson = JsonSerializer.Serialize(preservationApiImportJobResult);

            if (isComplete && !wasComplete)
            {
                var deposit = dbContext.Deposits.Single(d => d.MintedId == request.DepositId);
                deposit.Status = DepositStates.Preserved;
                deposit.Preserved = storageApiImportJobResult.DateFinished;
                deposit.PreservedBy = storageApiImportJobResult.CreatedBy!.GetSlug()!;
                deposit.LastModified = deposit.Preserved!.Value;
                deposit.LastModifiedBy = deposit.PreservedBy;
                deposit.VersionPreserved = storageApiImportJobResult.NewVersion;
            }
            
            await dbContext.SaveChangesAsync(cancellationToken);
            
            return Result.OkNotNull(preservationApiImportJobResult);
        }

        return importJobResultResult;
    }
    
    
    private static ImportJobResult Duplicate(ImportJobResult importJobResult)
    {
        var serialized = JsonSerializer.Serialize(importJobResult);
        return JsonSerializer.Deserialize<ImportJobResult>(serialized)!;
    }
}