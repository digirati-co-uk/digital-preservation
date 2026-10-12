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
    IImportJobResultStore importJobResultStore,
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration)
{
    // How often the heartbeat loop writes, not how long a missed heartbeat is tolerated for (that's
    // ImportJobResultStore.HeartbeatWindow, checked by the reaper, not here) - these are
    // deliberately two separate knobs.
    private TimeSpan HeartbeatInterval =>
        TimeSpan.FromSeconds(configuration.GetValue("ImportJobs:HeartbeatIntervalSeconds", 60));

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
        // The heartbeat loop runs alongside the job on a timer of its own, independent of the job's
        // progress - so it keeps going through a long single await (a slow Fedora commit, say) and
        // stops only when this job is done, not when it's merely quiet. It uses its own scope and
        // DbContext: it runs concurrently with everything below, and an EF context isn't thread-safe.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatLoop(jobIdentifier, heartbeatCts.Token);
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
        finally
        {
            await heartbeatCts.CancelAsync();
            await heartbeatTask;
        }

        // Conditional: if this job's heartbeat went quiet for long enough that it was reaped while
        // the code above was still actually running (a long GC pause, a debugger break, or similar),
        // the reaper has already written a completedWithErrors result for it. That record must win -
        // overwriting it with a late "completed" would hide the fact that the platform had already
        // reported, and possibly acted on, the job as failed.
        var finalUpdateResult = await importJobResultStore.SaveFinalImportJobResult(jobIdentifier, jobResult, cancellationToken);
        if (finalUpdateResult.Success && finalUpdateResult.Value)
        {
            logger.LogInformation("Saved Import Job Result: {JobResultId}", jobResult.Id);
        }
        else if (finalUpdateResult.Success)
        {
            logger.LogWarning(
                "Import job {JobIdentifier} was already reaped as abandoned before it actually finished; keeping the reaper's record, not this result",
                jobIdentifier);
        }
        else
        {
            logger.LogError("Failed to update final import job: {JobResultId}, {CodeAndMessage}", jobResult.Id, finalUpdateResult.CodeAndMessage());
        }
    }

    /// <summary>
    /// Writes an immediate heartbeat, then one roughly every <see cref="HeartbeatInterval"/>, until
    /// cancelled. A fresh scope (and so a fresh DbContext) per tick: this loop outlives any single
    /// EF operation and runs concurrently with the job's own DbContext usage elsewhere.
    /// </summary>
    private async Task RunHeartbeatLoop(string jobIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var scope = serviceScopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IImportJobResultStore>();
                var result = await store.Heartbeat(jobIdentifier, cancellationToken);
                if (result.Failure)
                {
                    logger.LogWarning("Heartbeat write failed for import job {JobIdentifier}: {CodeAndMessage}",
                        jobIdentifier, result.CodeAndMessage());
                }
                await Task.Delay(HeartbeatInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the job finished (or the host is stopping) and Execute's finally cancelled us.
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
