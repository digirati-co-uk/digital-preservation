using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
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
        
        // Is this right... 
        var agResult = await storageApi.GetArchivalGroup(request.Deposit.ArchivalGroup.AbsolutePath, null);
        if (agResult.Success || agResult.ErrorCode == ErrorCodes.NotFound)
        {
            var ag = agResult.Value; // OK to be null
        }

        throw new NotImplementedException();

    }
}