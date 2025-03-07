using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;

namespace Storage.API.Features.Import;

public interface IImportJobResultStore
{
    public Task<Result<ImportJob?>> GetImportJob(string jobIdentifier, CancellationToken cancellationToken);
    public Task<Result<ImportJobResult?>> GetImportJobResult(string jobIdentifier, CancellationToken cancellationToken);
    public Task<Result> SaveImportJob(string jobIdentifier, ImportJob importJob, CancellationToken cancellationToken);
    public Task<Result> SaveImportJobResult(string jobIdentifier, ImportJobResult importJobResult, bool active, bool ended, CancellationToken cancellationToken);
    public Task<Result<List<string>>> GetActiveJobsForArchivalGroup(Uri? importJobArchivalGroup, CancellationToken cancellationToken);
    Task<Result<int>> GetTotalImportJobs(CancellationToken cancellationToken);
    Task<Result<List<ImportJobResult>>> GetActivityPageOfResults(int page, int pageSize, CancellationToken cancellationToken);
}