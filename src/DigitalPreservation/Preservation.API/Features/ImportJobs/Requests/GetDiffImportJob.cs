using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetDiffImportJob(Deposit deposit) : IRequest<Result<ImportJob>>
{
    public Deposit Deposit { get; } = deposit;
}

public class GetDiffImportJobHandler(
    ILogger<GetDiffImportJobHandler> logger,
    IStorage storage,
    IStorageApiClient storageApi,
    ResourceMutator resourceMutator) : IRequestHandler<GetDiffImportJob, Result<ImportJob>>
{
    public async Task<Result<ImportJob>> Handle(GetDiffImportJob request, CancellationToken cancellationToken)
    {
        if (request.Deposit.ArchivalGroup == null)
        {
            return Result.FailNotNull<ImportJob>(ErrorCodes.BadRequest, "Deposit doesn't have Archival Group specified.");
        }
        
        var importJobResult = await storageApi.GetImportJob(
            request.Deposit.ArchivalGroup.GetPathUnderRoot()!,
            request.Deposit.Files!);
        

        if (importJobResult is { Success: true, Value: not null })
        {
            var storageImportJob = importJobResult.Value;
            if (!storageImportJob.IsUpdate && request.Deposit.ArchivalGroupName.HasText())
            {
                storageImportJob.ArchivalGroupName = request.Deposit.ArchivalGroupName;
            }
            storageImportJob.Deposit = request.Deposit.Id;
            
            var embellishResult = await storage.EmbellishImportJob(storageImportJob);
            if (embellishResult.Success)
            {
                // We don't put this in the DB here - only when we execute it.
                resourceMutator.MutateStorageImportJob(storageImportJob);
            }
        }
        return importJobResult;
    }
}