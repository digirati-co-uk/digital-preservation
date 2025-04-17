using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;
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
    IIdentityMinter identityMinter,
    IImportJobResultStore importJobResultStore,
    IImportJobQueue importJobQueue) : IRequestHandler<QueueImportJob, Result<ImportJobResult>>
{
    public async Task<Result<ImportJobResult>> Handle(QueueImportJob request, CancellationToken cancellationToken)
    {
        if (request.ImportJob.CreatedBy == null)
        {
            logger.LogError("Import Job {} does not have a createdBy", request.ImportJob.Id?.GetSlug());
            return Result.FailNotNull<ImportJobResult>(ErrorCodes.Unauthorized, 
                $"Cannot queue an importJob that lacks a createdBy: {request.ImportJob.ArchivalGroup}");
        }
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
        var jobIdentifier = identityMinter.MintIdentity(nameof(ImportJobResult));
        var saveJobResult = await importJobResultStore.SaveImportJob(jobIdentifier, request.ImportJob, cancellationToken);
        if (saveJobResult.Success)
        {
            var waitingResult = CreateWaitingResult(jobIdentifier, request.ImportJob);
            var saveResultResult =  await importJobResultStore.SaveImportJobResult(
                jobIdentifier, waitingResult, true, false, cancellationToken);
            if (saveResultResult.Success)
            {
                logger.LogInformation($"About to queue import job request {jobIdentifier}");
                try
                {
                    await importJobQueue.QueueRequest(jobIdentifier, cancellationToken);
                    return Result.OkNotNull(waitingResult);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not publish import job");
                    return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "Could not publish import job: " + e.Message);
                }
            }
        }
        return Result.FailNotNull<ImportJobResult>(ErrorCodes.UnknownError, "Unable to create and queue import job.");
    }

    private ImportJobResult CreateWaitingResult(string jobIdentifier, ImportJob importJob)
    {
        var callerIdentity = importJob.CreatedBy!.GetSlug()!;
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
            OriginalImportJob = importJob.OriginalId // what's the purpose of these when there's always a RESULT... are they the same?
        };
        
        return importJobResult;
    }
}