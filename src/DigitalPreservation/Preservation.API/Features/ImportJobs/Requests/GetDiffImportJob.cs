using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetDiffImportJob(Deposit deposit) : IRequest<Result<ImportJob>>
{
    public Deposit Deposit { get; } = deposit;
}

public class GetDiffImportJobHandler(
    ILogger<GetDiffImportJobHandler> logger,
    IStorageApiClient storageApi,
    PreservationContext dbContext,
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
            var importJob = importJobResult.Value;
            if (!importJob.IsUpdate && request.Deposit.ArchivalGroupName.HasText())
            {
                importJob.ArchivalGroupName = request.Deposit.ArchivalGroupName;
            }
            importJob.Deposit = request.Deposit.Id;
            resourceMutator.MutateImportJob(importJob);
        }
        return importJobResult;
    }
}