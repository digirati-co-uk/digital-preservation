using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using Microsoft.EntityFrameworkCore;
using Storage.API.Data;

namespace Storage.API.Features.Import.Data;

using Activity = DigitalPreservation.Common.Model.ChangeDiscovery.Activity;
using ImportJobEntity = Storage.API.Data.Entities.ImportJob;

public class ImportJobResultStore(
    StorageContext dbContext,
    ILogger<ImportJobResultStore> logger,
    IConfiguration configuration) : IImportJobResultStore
{
    // A job whose processor crashed or was killed mid-run (e.g. an ECS task restart) stops sending
    // heartbeats, which would otherwise wedge its Archival Group's imports behind a 409 Conflict
    // forever. Treat a running job (one that has sent at least one heartbeat) whose heartbeat is
    // older than this as abandoned. Configurable mainly so a long local debugging session (where a
    // breakpoint pauses the heartbeat loop too) can widen it rather than get reaped mid-pause.
    private TimeSpan HeartbeatWindow =>
        TimeSpan.FromMinutes(configuration.GetValue("ImportJobs:HeartbeatWindowMinutes", 10));


    public async Task<Result<int>> GetTotalImportJobs(CancellationToken cancellationToken)
    {
        try
        {
            var total = await dbContext.ImportJobs
                .Where(j => j.EndTime != null)
                .CountAsync(cancellationToken: cancellationToken);
            return Result.Ok(total); 
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<int>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<List<Activity>>> GetActivityPageOfResults(int page, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var entities = await dbContext.ImportJobs
                .AsNoTracking() // see the comment on the equivalent query in GetActiveJobsForArchivalGroup
                .Where(j => j.EndTime != null)
                .OrderBy(j => j.EndTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var importJobs = entities
                .Select(MakeActivity)
                .ToList();
            return Result.OkNotNull(importJobs); 
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<Activity>>(ErrorCodes.UnknownError, e.Message);
        }
    }

    internal static Activity MakeActivity(API.Data.Entities.ImportJob importJob)
    {
        // A job that created the Archival Group leaves SourceVersion null (ExecuteImportJob only
        // ever sets it for an update) and NewVersion set on success - the same distinction
        // Preservation API's own stream makes. A failed job (NewVersion null too) and a row with no
        // stored result both fall through to Update, since there is nothing to show was created.
        var activityType = ActivityTypes.Update;
        if (importJob.ImportJobResultJson != null)
        {
            var importJobResult = JsonSerializer.Deserialize<ImportJobResult>(importJob.ImportJobResultJson);
            if (importJobResult is { SourceVersion: null, NewVersion: not null })
            {
                activityType = ActivityTypes.Create;
            }
        }

        return new Activity
        {
            Type = activityType,
            Object = new ActivityObject
            {
                Id = importJob.ImportJobResultUri!,
                Type = nameof(ImportJobResult),
                SeeAlso =
                [
                    new ActivityObject
                    {
                        Id = importJob.ArchivalGroup,
                        Type = nameof(ArchivalGroup)
                    }
                ]
            },
            StartTime = importJob.Received,
            EndTime = importJob.EndTime!.Value
        };
    }

    public async Task<Result<List<string>>> GetActiveJobsForArchivalGroup(Uri? archivalGroup, CancellationToken cancellationToken)
    {
        try
        {
            // AsNoTracking: this read never mutates these entities via SaveChanges (reaping is a
            // raw conditional UPDATE below, invisible to the change tracker by design). Tracking
            // them here would plant a stale snapshot in this DbContext's identity map - any later
            // query on the same context (same request/scope) for a row this method just reaped
            // would then see pre-reap values instead of what the raw UPDATE actually wrote.
            var activeJobs = await dbContext.ImportJobs
                .AsNoTracking()
                .Where(ij => ij.ArchivalGroup == archivalGroup && ij.Active)
                .ToListAsync(cancellationToken);

            var jobIds = new List<string>();
            foreach (var job in activeJobs)
            {
                // A null heartbeat means the job has never started executing, i.e. it is still
                // queued - it is never reaped here by age. SQS retains a message for up to 14 days,
                // so it may simply not have been picked up yet; the dead-letter case (it never will
                // be) needs an operator path, not an automatic one, since nothing here can tell the
                // two apart. In-process mode's equivalent (the queue itself is lost on restart) is
                // handled separately, once, at startup - see FailOrphanedWaitingJobs.
                if (job.LastHeartbeat != null && await TryReapStaleRunningJob(job, cancellationToken))
                {
                    continue;
                }
                jobIds.Add(job.Id);
            }
            return Result.OkNotNull(jobIds);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.FailNotNull<List<string>>(ErrorCodes.UnknownError, e.Message);
        }
    }

    /// <summary>
    /// Periodic sweep across every Archival Group, not just the one a caller happens to be queuing
    /// into right now - <see cref="GetActiveJobsForArchivalGroup"/> alone leaves a group whose only
    /// job was abandoned blocked forever, because nothing then asks about that group again until a
    /// human intervenes (issue #354 review). Driven by <c>ImportJobReapService</c> on a timer.
    /// </summary>
    public async Task<Result<int>> ReapAllStaleRunningJobs(CancellationToken cancellationToken)
    {
        try
        {
            // AsNoTracking: see the comment on the equivalent query in GetActiveJobsForArchivalGroup.
            var activeJobs = await dbContext.ImportJobs
                .AsNoTracking()
                .Where(ij => ij.Active && ij.LastHeartbeat != null)
                .ToListAsync(cancellationToken);

            var reapedCount = 0;
            foreach (var job in activeJobs)
            {
                if (await TryReapStaleRunningJob(job, cancellationToken))
                {
                    reapedCount++;
                }
            }
            return Result.Ok(reapedCount);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<int>(ErrorCodes.UnknownError, e.Message);
        }
    }

    /// <summary>
    /// Attempts to reap one running job via a single conditional UPDATE guarded by the database's
    /// own clock (not this process's), so it cannot race a heartbeat or the runner's own final write
    /// landing between the read above and this write: if either already happened, the WHERE clause
    /// simply matches no rows, and this is a no-op rather than a lost update.
    /// </summary>
    private async Task<bool> TryReapStaleRunningJob(ImportJobEntity job, CancellationToken cancellationToken)
    {
        var abandonedResult = BuildAbandonedResult(job, $"abandoned: no heartbeat since {job.LastHeartbeat:O}");
        if (abandonedResult == null)
        {
            logger.LogError(
                "Cannot reap stale active import job {JobId}: no storable result to fail it with", job.Id);
            return false;
        }

        var resultJson = JsonSerializer.Serialize(abandonedResult);
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_jobs
            SET active = false,
                import_job_result_json = {resultJson},
                end_time = {abandonedResult.DateFinished},
                import_job_result_uri = {abandonedResult.Id}
            WHERE id = {job.Id}
              AND active
              AND last_heartbeat < now() - {HeartbeatWindow}
            """, cancellationToken);

        if (rowsAffected > 0)
        {
            logger.LogWarning(
                "Reaped stale active import job {JobId} for Archival Group {ArchivalGroup}: no heartbeat since {LastHeartbeat}",
                job.Id, job.ArchivalGroup, job.LastHeartbeat);
            return true;
        }
        return false;
    }

    public async Task<Result<int>> FailOrphanedWaitingJobs(string reason, CancellationToken cancellationToken)
    {
        try
        {
            var orphaned = await dbContext.ImportJobs
                .AsNoTracking() // see the comment on the equivalent query in GetActiveJobsForArchivalGroup
                .Where(ij => ij.Active && ij.LastHeartbeat == null)
                .ToListAsync(cancellationToken);

            var failedCount = 0;
            foreach (var job in orphaned)
            {
                var result = BuildAbandonedResult(job, reason);
                if (result == null)
                {
                    logger.LogError(
                        "Cannot fail orphaned waiting import job {JobId}: no storable result to fail it with", job.Id);
                    continue;
                }
                var resultJson = JsonSerializer.Serialize(result);
                var rowsAffected = await dbContext.ImportJobs
                    .Where(ij => ij.Id == job.Id && ij.Active)
                    .ExecuteUpdateAsync(setters => setters
                            .SetProperty(ij => ij.Active, false)
                            .SetProperty(ij => ij.ImportJobResultJson, resultJson)
                            .SetProperty(ij => ij.EndTime, result.DateFinished)
                            .SetProperty(ij => ij.ImportJobResultUri, result.Id),
                        cancellationToken);
                if (rowsAffected > 0)
                {
                    failedCount++;
                    logger.LogWarning("Failed orphaned waiting import job {JobId} at startup: {Reason}", job.Id, reason);
                }
            }
            return Result.Ok(failedCount);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<int>(ErrorCodes.UnknownError, e.Message);
        }
    }

    /// <summary>
    /// Builds a completedWithErrors result from a job's own last-known stored result (so it keeps
    /// the real ArchivalGroup/Deposit/CreatedBy/Id etc.), for recording an abandoned job. Null if
    /// there is nothing to build from - a job with no result on file at all, which SaveImportJob's
    /// paired initial write should always have produced, but a method with external callers should
    /// not assume.
    /// </summary>
    private static ImportJobResult? BuildAbandonedResult(ImportJobEntity job, string reason)
    {
        if (job.ImportJobResultJson == null)
        {
            return null;
        }
        var result = JsonSerializer.Deserialize<ImportJobResult>(job.ImportJobResultJson);
        if (result == null)
        {
            return null;
        }
        result.Status = ImportJobStates.CompletedWithErrors;
        result.DateFinished = DateTime.UtcNow;
        result.Errors = [.. result.Errors ?? [], new Error { Message = reason }];
        return result;
    }

    public async Task<Result> Heartbeat(string jobIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            // The database's own clock, not this process's: the importer writing the beat and the
            // Storage API comparing it later may be on different machines.
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE import_jobs
                SET last_heartbeat = now()
                WHERE id = {jobIdentifier} AND active
                """, cancellationToken);
            return Result.Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<bool>> SaveFinalImportJobResult(
        string jobIdentifier, ImportJobResult importJobResult, CancellationToken cancellationToken)
    {
        try
        {
            var resultJson = JsonSerializer.Serialize(importJobResult);
            var rowsAffected = await dbContext.ImportJobs
                .Where(ij => ij.Id == jobIdentifier && ij.Active)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(ij => ij.Active, false)
                        .SetProperty(ij => ij.ImportJobResultJson, resultJson)
                        .SetProperty(ij => ij.EndTime, importJobResult.DateFinished)
                        .SetProperty(ij => ij.ImportJobResultUri, importJobResult.Id),
                    cancellationToken);
            return Result.Ok(rowsAffected > 0);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<bool>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<bool>> SaveRunningImportJobResult(
        string jobIdentifier, ImportJobResult importJobResult, CancellationToken cancellationToken)
    {
        try
        {
            var resultJson = JsonSerializer.Serialize(importJobResult);
            // Deliberately does not SetProperty(Active, true): the row is either already Active (the
            // common case - this just records progress) or it was reaped out from under this job
            // between dequeue and here, in which case the WHERE below matches no rows and this must
            // stay a no-op rather than resurrecting it.
            var rowsAffected = await dbContext.ImportJobs
                .Where(ij => ij.Id == jobIdentifier && ij.Active)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(ij => ij.ImportJobResultJson, resultJson),
                    cancellationToken);
            return Result.Ok(rowsAffected > 0);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<bool>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> SaveImportJob(string jobIdentifier, ImportJob importJob, CancellationToken cancellationToken)
    {
        if (importJob.ArchivalGroup == null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Ingest Job must specify an archival group");
        }

        try
        {
            await dbContext.ImportJobs.AddAsync(
                new API.Data.Entities.ImportJob
                {
                    Id = jobIdentifier, 
                    ArchivalGroup = importJob.ArchivalGroup,
                    ImportJobJson = JsonSerializer.Serialize(importJob),
                    Active = true,
                    Received = DateTime.UtcNow
                }, 
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result> SaveImportJobResult(
        string jobIdentifier, 
        ImportJobResult importJobResult, 
        bool active, 
        bool ended,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.ImportJobs.FindAsync([jobIdentifier], cancellationToken);
            if (entity == null)
            {
                return Result.Fail(ErrorCodes.NotFound, $"Import job {jobIdentifier} could not be found");
            }
            entity.ImportJobResultJson = JsonSerializer.Serialize(importJobResult);
            entity.Active = active;
            if (ended)
            {
                entity.EndTime = importJobResult.DateFinished!;
                entity.ImportJobResultUri = importJobResult.Id;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }


    public async Task<Result<ImportJob?>> GetImportJob(string jobIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.ImportJobs.AsNoTracking().SingleOrDefaultAsync(ij => ij.Id == jobIdentifier, cancellationToken);
            if (entity == null)
            {
                return Result.Fail<ImportJob>(ErrorCodes.NotFound, $"Import job {jobIdentifier} could not be found");
            }
            var importJob = JsonSerializer.Deserialize<ImportJob>(entity.ImportJobJson);
            if (importJob == null)
            {
                return Result.Fail<ImportJob>(ErrorCodes.UnknownError, $"Import job {jobIdentifier} has no JSON body in storage.");
            }
            return Result.Ok(importJob);
        }
        catch (Exception e)
        {
            return Result.Fail<ImportJob>(ErrorCodes.UnknownError, e.Message);
        }
    }

    public async Task<Result<ImportJobResult?>> GetImportJobResult(string jobIdentifier, CancellationToken cancellationToken)
    {        
        try
        {
            var entity = await dbContext.ImportJobs.AsNoTracking().SingleOrDefaultAsync(ij => ij.Id == jobIdentifier, cancellationToken);
            if (entity == null)
            {
                return Result.Fail<ImportJobResult>(ErrorCodes.NotFound, $"Import job {jobIdentifier} could not be found");
            }
            if (entity.ImportJobResultJson == null)
            {
                return Result.Fail<ImportJobResult>(ErrorCodes.UnknownError, $"Result for Import Job {jobIdentifier} has no JSON body in storage.");
            }
            var importJobResult = JsonSerializer.Deserialize<ImportJobResult>(entity.ImportJobResultJson);
            if (importJobResult == null)
            {
                return Result.Fail<ImportJobResult>(ErrorCodes.UnknownError, $"Result for Import Job {jobIdentifier} could not be deserialised.");
            }
            return Result.Ok(importJobResult);
        }
        catch (Exception e)
        {
            return Result.Fail<ImportJobResult>(ErrorCodes.UnknownError, e.Message);
        }
    }
}