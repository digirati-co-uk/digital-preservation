using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class GetImportJobResult(string id) : IRequest<Result<ImportJobResult>>
{
    public string Id { get; } = id;
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
        // importJobEntity
        
        // read its storage API URl
        // ask the storage API for its version, get its status
        var x = await storageApi.GetImportJobResult(importJobEntity.StorageImportJobId);
        
        // merge? query? update? the DB for the preservation api version
        // populate the results parts of the importJobEntity (if we are done)
        
        throw new NotImplementedException();
        
    }
}