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
        var entity = await dbContext.PipelineRunJobs.SingleAsync(
            d => d.Deposit == request.PipelineDeposit.DepositId && d.Id == request.PipelineDeposit.Id, cancellationToken);

        if (request.PipelineDeposit.Status == PipelineJobStates.Running)
        {
            // Starting a job is a CLAIM, not a status report: the job moves out of "waiting" exactly
            // once, and whoever makes that move is the one run that may proceed (issue #221). The
            // pipeline is driven by SNS/SQS, which is at-least-once, so the same start message can
            // arrive more than once for one job; without this, each delivery would run Brunnhilde
            // again and append another virus-scan provenance event for a scan that only happened once.
            return await ClaimJob(request, deposit.MintedId, cancellationToken);
        }

        switch (request.PipelineDeposit.Status)
        {
            case PipelineJobStates.Waiting:
                // The PipelineRunJob must already exist
                break;
            case PipelineJobStates.MetadataCreated:
                break;
            case PipelineJobStates.Completed:
                entity.DateFinished = DateTime.UtcNow;
                break;
            case PipelineJobStates.CompletedWithErrors:
                entity.DateFinished = DateTime.UtcNow;
                entity.Errors = request.PipelineDeposit.Errors;
                break;
        }
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
            // The job exists - it was loaded above - so it is simply no longer waiting.
            logger.LogWarning(
                "Pipeline job {JobId} for deposit {DepositId} was asked to start but is not waiting; " +
                "treating this as a repeat delivery and refusing the claim",
                request.PipelineDeposit.Id, depositId);

            return Result.Fail(ErrorCodes.Conflict,
                $"Pipeline job {request.PipelineDeposit.Id} is not waiting to be run, so it cannot be started again.");
        }

        logger.LogInformation("Pipeline job {JobId} for deposit {DepositId} claimed by {CallerIdentity}",
            request.PipelineDeposit.Id, depositId, request.User.GetCallerIdentity());
        return Result.Ok();
    }
}