using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import.Requests;

public class QueueImportJob(ImportJob importJob) : IRequest<Result<ImportJobResult>>
{
    public ImportJob ImportJob { get; } = importJob;
}

public class QueueImportJobHandler(
    ILogger<QueueImportJobHandler> logger,
    Converters converters,
    IIdentityService identityService,
    IImportJobResultStore importJobResultStore,
    IImportJobQueue importJobQueue) : IRequestHandler<QueueImportJob, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(QueueImportJob request, CancellationToken cancellationToken)
    {
        var activeImportJobs = await importJobResultStore.GetActiveJobsForArchivalGroup(request.ImportJob.ArchivalGroup, cancellationToken);
        if (activeImportJobs.Success && activeImportJobs.Value!.Count > 0)
        {
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.Conflict, 
                $"There is already an active import job ({activeImportJobs.Value[0]}) for Archival Group {request.ImportJob.ArchivalGroup}");
        }
        if (activeImportJobs.Failure)
        {
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, 
                $"Could not check for active import jobs for Archival Group {request.ImportJob.ArchivalGroup}");
        }
        var jobIdentifier = identityService.MintIdentity(nameof(ImportJobResult));
        var saveJobResult = await importJobResultStore.SaveImportJob(jobIdentifier, request.ImportJob, cancellationToken);
        if (saveJobResult.Success)
        {
            var waitingResult = CreateWaitingResult(jobIdentifier, request.ImportJob);
            var saveResultResult =  await importJobResultStore.SaveImportJobResult(jobIdentifier, waitingResult, true, cancellationToken);
            if (saveResultResult.Success)
            {
                logger.LogInformation($"About to queue import job request {jobIdentifier}");
                await importJobQueue.QueueRequest(jobIdentifier, cancellationToken);
                return Result.OkNotNull(waitingResult);
            }
        }
        return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "Unable to create and queue import job.");
    }

    private ImportJobResult CreateWaitingResult(string jobIdentifier, ImportJob importJob)
    {
        var callerIdentity = "dlipdev";
        var now = DateTime.UtcNow;
        
        var importJobResult = new ImportJobResult
        {
            Id = converters.GetStorageImportJobResultId(importJob.ArchivalGroup.GetPathUnderRoot()!, jobIdentifier),
            Status = ImportJobStates.Waiting,
            ArchivalGroup = importJob.ArchivalGroup!,
            SourceVersion = null, // set in executor
            DateBegun = null,     // set in executor
            Created = now,
            CreatedBy = converters.GetAgentUri(callerIdentity),
            LastModified = now,
            LastModifiedBy = converters.GetAgentUri(callerIdentity),
            ImportJob = importJob.Id!, // NB this may be the Preservation API's import job ID - which is OK
            OriginalImportJob = importJob.Id!, // what's the purpose of these when there's always a RESULT... are they the same?
        };
        
        return importJobResult;
    }
}