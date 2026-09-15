using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using MediatR;
using Storage.API.Features.Import.Requests;
using Storage.API.Features.Repository.Requests;
using Storage.API.Fedora.Model;

namespace Storage.API.Features.Import;

/// <summary>
/// Runs one dequeued import job to a terminal state. Whatever happens - the job fails, the executor
/// throws, Fedora cannot be reached afterwards - the stored result must end up not active, because
/// an active result blocks every later job for the same Archival Group with a 409, and the queue
/// message is already gone by the time we get here, so there is no retry to fall back on.
/// </summary>
public class ImportJobRunner(
    ILogger<ImportJobRunner> logger,
    IMediator mediator,
    IImportJobResultStore importJobResultStore)
{
    public async Task Execute(string jobIdentifier, CancellationToken cancellationToken)
    {
        // Should all this go inside ExecuteImportJob?
        // It needs to save the ImportJobResult itself to update it.
        var importJob = await importJobResultStore.GetImportJob(jobIdentifier, cancellationToken);
        var initialResult = await importJobResultStore.GetImportJobResult(jobIdentifier, cancellationToken);
        if (importJob.Failure || initialResult.Failure || importJob.Value == null || initialResult.Value == null)
        {
            logger.LogError("Unable to load Import Job {JobIdentifier}: {CodeAndMessage}", jobIdentifier,
                importJob.Failure ? importJob.CodeAndMessage() : initialResult.CodeAndMessage());
            return;
        }

        var jobResult = initialResult.Value;
        try
        {
            var executeResult = await mediator.Send(new ExecuteImportJob(jobIdentifier, importJob.Value, jobResult), cancellationToken);
            if (executeResult.Success)
            {
                jobResult = executeResult.Value!;
                await RecordNewVersion(jobIdentifier, jobResult, cancellationToken);
            }
            else
            {
                logger.LogError("Unable to execute Import Job Result, and did not fail early cleanly: {CodeAndMessage}",
                    executeResult.CodeAndMessage());
                MarkFailed(jobResult, executeResult.ErrorMessage ?? "Import job could not be executed");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Import job {JobIdentifier} threw; recording it as failed", jobIdentifier);
            MarkFailed(jobResult, e.Message);
        }

        var finalUpdateResult = await importJobResultStore.SaveImportJobResult(jobIdentifier, jobResult, false, true, cancellationToken);
        if (finalUpdateResult.Success)
        {
            logger.LogInformation("Saved Import Job Result: {JobResultId}", jobResult.Id);
        }
        else
        {
            logger.LogError("Failed to update final import job: {JobResultId}, {CodeAndMessage}", jobResult.Id, finalUpdateResult.CodeAndMessage());
        }
    }

    /// <summary>
    /// The job itself may have failed at this point, but the result is still a success: what version
    /// are we now on? This may be unchanged if the job itself failed.
    /// </summary>
    private async Task RecordNewVersion(string jobIdentifier, ImportJobResult jobResult, CancellationToken cancellationToken)
    {
        var pathUnderRoot = jobResult.ArchivalGroup.GetPathUnderRoot();
        if (!SafeRepositoryPath.IsRepositoryPath(pathUnderRoot, out var reason))
        {
            // The executor has already refused this job for the same reason (a job stored before the
            // rule existed, say); asking Fedora for the group would only throw on the same path.
            logger.LogWarning("Not fetching Archival Group for {JobIdentifier}: {Path} is not a path under the root ({Reason})",
                jobIdentifier, pathUnderRoot, reason);
            return;
        }

        logger.LogInformation("ImportJobRunner.Execute for {JobIdentifier} has returned, will try to obtain {PathUnderRoot} from Fedora.",
            jobIdentifier, pathUnderRoot);
        var agResult = await mediator.Send(new GetResourceFromFedora(pathUnderRoot), cancellationToken);
        if (agResult.Success)
        {
            if (agResult.Value is ArchivalGroup ag)
            {
                jobResult.NewVersion = ag.Version!.OcflVersion;
                logger.LogInformation("Import Job new version is {NewVersion} for {JobResultId}", jobResult.NewVersion, jobResult.Id);
            }
            else
            {
                logger.LogError("Resource is not an Archival Group: {Resource}", agResult.Value);
            }
        }
        else
        {
            logger.LogError("Unable to obtain saved Archival Group (maybe because of a failed create): {CodeAndMessage}", agResult.CodeAndMessage());
        }
    }

    private static void MarkFailed(ImportJobResult jobResult, string message)
    {
        jobResult.Status = ImportJobStates.CompletedWithErrors;
        jobResult.DateFinished ??= DateTime.UtcNow;
        jobResult.Errors = [.. jobResult.Errors ?? [], new Error { Message = message }];
    }
}
