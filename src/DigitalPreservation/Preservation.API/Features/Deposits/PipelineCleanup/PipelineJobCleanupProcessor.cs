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
            // directly instead, both here and in the conditional update below.
            var overdueJobs = await dbContext.PipelineRunJobs
                .Where(j => j.Status != PipelineJobStates.Completed && j.Status != PipelineJobStates.CompletedWithErrors)
                .Where(j => (j.DateBegun != null && j.DateBegun < cutoff)
                            || (j.DateBegun == null && j.DateSubmitted != null && j.DateSubmitted < cutoff))
                .ToListAsync(cancellationToken);

            foreach (var job in overdueJobs)
            {
                // "Latest" is whichever job for this deposit was most recently submitted - every job
                // gets DateSubmitted set at creation (RunPipelineHandler), so this needs no null
                // handling, unlike DateBegun (never set for a job still waiting).
                var isLatestForDeposit = !await dbContext.PipelineRunJobs
                    .AnyAsync(j => j.Deposit == job.Deposit && j.DateSubmitted > job.DateSubmitted, cancellationToken);

                if (!isLatestForDeposit)
                {
                    await CloseWithoutReleasing(job.Id, job.Deposit, cancellationToken);
                }
                else
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
