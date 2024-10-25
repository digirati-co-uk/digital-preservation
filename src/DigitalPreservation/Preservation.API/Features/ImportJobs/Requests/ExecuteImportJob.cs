using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.ImportJobs.Requests;

public class ExecuteImportJob(ImportJob importJob) : IRequest<Result<ImportJobResult>>
{
    public ImportJob ImportJob { get; } = importJob;
}

public class ExecuteImportJobHandler(
    ILogger<ExecuteImportJobHandler> logger,
    IStorageApiClient storageApi,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IStorage storage) : IRequestHandler<ExecuteImportJob, Result<ImportJobResult>>
{
    public Task<Result<ImportJobResult>> Handle(ExecuteImportJob request, CancellationToken cancellationToken)
    {
        // Call the storage API to get its importjobresult
        // put it in the local DB
        throw new NotImplementedException();
    }
}