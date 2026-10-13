using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Preservation.API.Features.Deposits.Requests;

namespace Preservation.API.Features.Deposits.PipelineCleanup;

public class PipelineJobCleanupProcessor(
    PreservationContext dbContext,
    IOptions<PipelineOptions> pipelineOptions,
    IMediator mediator,
    ILogger<PipelineJobCleanupProcessor> logger)
{
    private const string CleanupMessage = "Cleaned up as previous processing did not complete";

    public async Task<Result> CleanupOverdueJobs(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-pipelineOptions.Value.PipelineJobsCleanupMinutes);

            // Not PipelineJobStates.IsNotComplete(j.Status) here: EF Core cannot translate an
            // arbitrary static method call into SQL, so the two terminal statuses are compared
            // directly instead, both here and in the conditional updates below.
            var nonTerminalJobs = await dbContext.PipelineRunJobs
                .Where(j => j.Status != PipelineJobStates.Completed && j.Status != PipelineJobStates.CompletedWithErrors)
                .ToListAsync(cancellationToken);

            foreach (var job in nonTerminalJobs)
            {
                // "Latest" is whichever job for this deposit was most recently submitted - every job
                // gets DateSubmitted set at creation (RunPipelineHandler), so this needs no null
                // handling, unlike DateBegun (never set for a job still waiting). The same definition
                // RunPipelineHandler itself now uses to decide what blocks a new run (issue #357).
                var isLatestForDeposit = !await dbContext.PipelineRunJobs
                    .AnyAsync(j => j.Deposit == job.Deposit && j.DateSubmitted > job.DateSubmitted, cancellationToken);

                if (!isLatestForDeposit)
                {
                    // Closed immediately, with no overdue cutoff: a non-latest job is superseded the
                    // moment a newer one exists for the same deposit, so there is nothing to wait for
                    // (issue #357) - leaving it active for up to PipelineJobsCleanupMinutes blocks
                    // RunPipelineHandler's "is the deposit's latest job active" check for no reason
                    // and leaves the UI with a job it can neither show as running nor force-complete.
                    await CloseWithoutReleasing(job.Id, job.Deposit, cancellationToken);
                    continue;
                }

                var isOverdue = (job.DateBegun != null && job.DateBegun < cutoff)
                                 || (job.DateBegun == null && job.DateSubmitted != null && job.DateSubmitted < cutoff);
                if (isOverdue)
                {
                    await CloseAndReleaseIfHeld(job, cancellationToken);
                }
            }

            return Result.Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Pipeline job cleanup sweep failed");
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }
    }

    /// <summary>
    /// A newer job already exists for this deposit, so its lock - if this job's own RunUser still
    /// holds it - belongs to that newer run now (issue #299's "transfer" decision), not to this stale
    /// one. Closes the job out without touching the lock at all, via a conditional update rather than
    /// RunPipelineStatusHandler's read-then-write, so two sweepers racing on the same job can't both
    /// act on it (issue #301, adversarial review of #309).
    /// </summary>
    private async Task CloseWithoutReleasing(string jobId, string depositId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await dbContext.PipelineRunJobs
            .Where(j => j.Id == jobId
                        && j.Status != PipelineJobStates.Completed && j.Status != PipelineJobStates.CompletedWithErrors)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Status, PipelineJobStates.CompletedWithErrors)
                .SetProperty(j => j.DateFinished, now)
                .SetProperty(j => j.Errors, CleanupMessage)
                .SetProperty(j => j.LastUpdated, now), cancellationToken);

        if (updated > 0)
        {
            logger.LogInformation(
                "Cleaned up stale pipeline job {JobId} for deposit {DepositId} without releasing the lock: a newer job exists",
                jobId, depositId);
        }
    }

    /// <summary>
    /// This is the deposit's most recent job, so the normal terminal transition may release the lock
    /// - through RunPipelineStatusHandler, the single release path from issue #299, rather than a
    /// second copy of it here. That handler releases only if the deposit is still locked by this
    /// job's own RunUser, and only on the transition into terminal (not a repeat report).
    /// </summary>
    private async Task CloseAndReleaseIfHeld(PipelineRunJob job, CancellationToken cancellationToken)
    {
        var pipelineDeposit = new PipelineDeposit
        {
            Id = job.Id,
            Status = PipelineJobStates.CompletedWithErrors,
            DepositId = job.Deposit,
            RunUser = job.RunUser,
            Errors = CleanupMessage
        };

        var result = await mediator.Send(new RunPipelineStatus(pipelineDeposit, new ClaimsPrincipal()), cancellationToken);
        if (result.Failure)
        {
            if (result.ErrorCode == ErrorCodes.NotFound)
            {
                // The deposit itself no longer exists - DeleteDeposit does not check for an active
                // pipeline job (issue #357) - so RunPipelineStatusHandler refuses before it can touch
                // the job at all. There is no deposit left to release a lock on, which is exactly
                // CloseWithoutReleasing's job; without this fallback the job sits non-terminal and
                // gets re-swept, and re-refused, on every tick for ever.
                logger.LogInformation(
                    "Pipeline job {JobId}'s deposit {DepositId} no longer exists; closing it out without releasing a lock",
                    job.Id, job.Deposit);
                await CloseWithoutReleasing(job.Id, job.Deposit, cancellationToken);
                return;
            }

            logger.LogWarning(
                "Could not clean up stale pipeline job {JobId} for deposit {DepositId}: {CodeAndMessage}",
                job.Id, job.Deposit, result.CodeAndMessage());
        }
        else
        {
            logger.LogInformation(
                "Cleaned up stale pipeline job {JobId} for deposit {DepositId}", job.Id, job.Deposit);
        }
    }
}
