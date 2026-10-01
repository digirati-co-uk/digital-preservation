using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Utils;

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipelineStatus(PipelineDeposit pipelineDeposit, ClaimsPrincipal user) : IRequest<Result>
{
    public PipelineDeposit PipelineDeposit { get; } = pipelineDeposit;
    public ClaimsPrincipal User { get; } = user;
}

public class RunPipelineStatusHandler(
    ILogger<RunPipelineStatusHandler> logger,
    PreservationContext dbContext) : IRequestHandler<RunPipelineStatus, Result>
{
    public async Task<Result> Handle(RunPipelineStatus request, CancellationToken cancellationToken)
    {
        var deposit = await dbContext.Deposits.SingleOrDefaultAsync(
            d => d.MintedId == request.PipelineDeposit.DepositId, cancellationToken);

        if (deposit == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for deposit id " + request.PipelineDeposit.DepositId);
        }
        if (request.PipelineDeposit.Status == PipelineJobStates.Running)
        {
            // Starting a job is a CLAIM, not a status report: the job moves out of "waiting" exactly
            // once, and whoever makes that move is the one run that may proceed (issue #221). The
            // pipeline is driven by SNS/SQS, which is at-least-once, so the same start message can
            // arrive more than once for one job; without this, each delivery would run Brunnhilde
            // again and append another virus-scan provenance event for a scan that only happened once.
            // Deliberately ahead of the load below: ClaimJob's own WHERE clause carries the whole
            // correctness guarantee, so loading the row first would be a round-trip that decides
            // nothing - and SingleAsync would throw on a job that no longer exists, where the claim
            // reports a clean Conflict and the delivery is abandoned as it should be.
            return await ClaimJob(request, deposit.MintedId, cancellationToken);
        }

        if (request.PipelineDeposit.Status.HasText() && PipelineJobStates.IsComplete(request.PipelineDeposit.Status))
        {
            // The one place every way a run ends goes through: success, failure, force complete
            // from the UI, the UI's stale-job tidy-up, and now the Preservation API's own sweep
            // (issue #301) all post a terminal status here.
            return await CompleteJob(request, cancellationToken);
        }

        // Waiting/MetadataCreated reports have no lock implications and nothing to race on a
        // repeat delivery, so this is unchanged from before issue #316.
        var entity = await dbContext.PipelineRunJobs.SingleAsync(
            d => d.Deposit == request.PipelineDeposit.DepositId && d.Id == request.PipelineDeposit.Id, cancellationToken);

        if (request.PipelineDeposit.Status.HasText())
        {
            entity.Status = request.PipelineDeposit.Status;
        }
        dbContext.PipelineRunJobs.Update(entity);

        try
        {
            logger.LogInformation("Saving Pipeline Job entity {EntityId} to DB for deposit {MintedId}", entity.Id, deposit.MintedId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Issue saving the Pipeline run job state.");
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }

        var callerIdentity = request.User.GetCallerIdentity();
        logger.LogInformation("Pipeline job {EntityId} was updated by {CallerIdentity}", entity.Id, callerIdentity);
        return Result.Ok();
    }

    /// <summary>
    /// Moves a job into a terminal status and, only if that move actually happened, releases the
    /// deposit's lock - both decided by the database rather than a value this handler read earlier
    /// (issue #316, hardening #299's release path against three races: a force-lock taken by someone
    /// else between read and save; two terminal reports for the same job landing together; and a
    /// late report overwriting a status someone already force-completed). Preservation API runs as
    /// more than one ECS task, so "earlier" and "later" can be two different processes.
    /// </summary>
    private async Task<Result> CompleteJob(RunPipelineStatus request, CancellationToken cancellationToken)
    {
        var depositId = request.PipelineDeposit.DepositId;
        var jobId = request.PipelineDeposit.Id;
        var newStatus = request.PipelineDeposit.Status!;
        var now = DateTime.UtcNow;

        // RunUser is set once, at job creation, and never changes - reading it ahead of the
        // conditional move below (rather than from whatever row it affected) is safe either way,
        // and means the move and the release each need only the one query the issue specifies.
        var runUser = await dbContext.PipelineRunJobs
            .Where(j => j.Deposit == depositId && j.Id == jobId)
            .Select(j => j.RunUser)
            .SingleAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Only a job not already terminal may move. A repeat report (force complete, then the
        // running job itself reporting afterwards) matches no row here and is treated as success,
        // not an error - decided by the database instead of a wasAlreadyTerminal value read earlier,
        // which is what let two concurrent reports both see "not yet terminal" and both release.
        var notYetTerminal = dbContext.PipelineRunJobs.Where(j => j.Deposit == depositId && j.Id == jobId
            && j.Status != PipelineJobStates.Completed && j.Status != PipelineJobStates.CompletedWithErrors);
        var moved = newStatus == PipelineJobStates.CompletedWithErrors
            ? await notYetTerminal.ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, newStatus)
                .SetProperty(j => j.DateFinished, now)
                .SetProperty(j => j.Errors, request.PipelineDeposit.Errors), cancellationToken)
            : await notYetTerminal.ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, newStatus)
                .SetProperty(j => j.DateFinished, now), cancellationToken);

        if (moved > 0)
        {
            // Only if the move above actually happened, and only if the deposit is still locked by
            // this job's own RunUser - someone else may have force-taken the lock mid-run
            // (POST /lock?force=true), and releasing theirs would be wrong.
            await dbContext.Deposits
                .Where(d => d.MintedId == depositId && d.LockedBy == runUser)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.LockedBy, (string?)null)
                    .SetProperty(d => d.LockDate, (DateTime?)null), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var callerIdentity = request.User.GetCallerIdentity();
        logger.LogInformation(
            "Pipeline job {JobId} for deposit {DepositId} reported {Status} by {CallerIdentity}; moved={Moved}",
            jobId, depositId, newStatus, callerIdentity, moved > 0);
        return Result.Ok();
    }

    /// <summary>
    /// Move a job from "waiting" to "processing", but only if it is still waiting. Returns Conflict
    /// when it is not, which tells the caller that this job is already being run - or has already
    /// been run - by someone else, and that it should abandon this delivery rather than repeat it.
    /// </summary>
    private async Task<Result> ClaimJob(RunPipelineStatus request, string depositId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // A single conditional UPDATE, so that two consumers racing on the same job cannot both
        // read "waiting" and both proceed. Read-then-write through the change tracker would leave
        // exactly that window open.
        var claimed = await dbContext.PipelineRunJobs
            .Where(job => job.Deposit == depositId
                          && job.Id == request.PipelineDeposit.Id
                          && job.Status == PipelineJobStates.Waiting)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, PipelineJobStates.Running)
                .SetProperty(job => job.DateBegun, now)
                .SetProperty(job => job.LastUpdated, now), cancellationToken);

        if (claimed == 0)
        {
            // Nothing matched, which is either "not waiting" or "not there". The caller does the same
            // thing in both cases - abandon the delivery - so this costs an extra round trip only on
            // the branch that is already the exceptional one, and buys a log line that says which.
            var exists = await dbContext.PipelineRunJobs.AnyAsync(
                job => job.Deposit == depositId && job.Id == request.PipelineDeposit.Id, cancellationToken);

            logger.LogWarning(
                "Pipeline job {JobId} for deposit {DepositId} was asked to start but {Reason}; " +
                "treating this as a repeat delivery and refusing the claim",
                request.PipelineDeposit.Id, depositId,
                exists ? "it is not waiting" : "no such job exists");

            return Result.Fail(ErrorCodes.Conflict,
                $"Pipeline job {request.PipelineDeposit.Id} is not waiting to be run, so it cannot be started again.");
        }

        logger.LogInformation("Pipeline job {JobId} for deposit {DepositId} claimed by {CallerIdentity}",
            request.PipelineDeposit.Id, depositId, request.User.GetCallerIdentity());
        return Result.Ok();
    }
}