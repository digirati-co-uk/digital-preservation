using Amazon.S3.Util;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalPreservation.Core.Auth;
using Preservation.API.Data.Entities;
using Preservation.API.Features.Deposits.PipelineDelivery;
using Storage.Repository.Common;

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipeline(string depositId, ClaimsPrincipal user) : IRequest<Result>
{
    public readonly ClaimsPrincipal User = user;
    public string DepositId { get; } = depositId;

}

public class RunPipelineHandler(
    ILogger<RunPipelineHandler> logger,
    PreservationContext dbContext,
    IAmazonSimpleNotificationService snsClient,
    IOptions<PipelineOptions> pipelineOptions,
    IOptions<AwsStorageOptions> storageOptions,
    IOptions<PipelineDeliveryFlags> pipelineDeliveryFlags,
    IIdentityMinter identityMinter,
    IPipelineJobDeliveryClient? pipelineJobDeliveryClient = null) : IRequestHandler<RunPipeline, Result>
{
    public async Task<Result> Handle(RunPipeline request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.DepositId, cancellationToken);
        if (entity == null)
        {
            return Result.Fail(ErrorCodes.NotFound,
                "Could not run pipeline because could not find no deposit for ID " + request.DepositId);
        }

        // Ahead of the lock: a 400 here must never leave a lock behind.
        // Pipeline.API reaches deposit files through a filesystem mount of the default working
        // bucket only, so a deposit routed to a per-caller bucket (RFC-0001 §8) can't be
        // characterised - decline it here, the only place pipeline jobs are queued from.
        if (!IsInDefaultWorkingBucket(entity.Files))
        {
            return Result.Fail(ErrorCodes.BadRequest,
                $"Could not run pipeline: deposit {request.DepositId} is not in the default " +
                $"working bucket '{storageOptions.Value.DefaultWorkingBucket}', which is the only " +
                "storage the pipeline can reach.");
        }

        // Also ahead of the lock, for the same reason: a 409 here must never leave a lock behind
        // either. The lock holder queuing a second run while the first is still going (a
        // double-submit, a script, two browser tabs) would otherwise both runs write to the same
        // metadata/ tool-output folders at once, and run 1 finishing would release the lock run 2 is
        // relying on, since both share the same RunUser (issue #317, from the adversarial review of
        // #309). A job stuck in "processing" because its worker died blocks new runs until it's
        // cleaned up (force complete, the UI's stale-job tidy-up, or #301's sweep) - that is the
        // right behaviour, not a bug to work around here.
        //
        // Blocks on ANY active job for the deposit, not only the latest one (issue #357 review):
        // #353's partial unique index on (deposit, status IN active) enforces exactly this - at most
        // one active job per deposit, ever - so a "latest job only" read check here would let the
        // read pass and then hit that index on insert, turning a clean 409 into an unhandled
        // constraint violation. It also matches the UI, which shows whichever job is still running
        // regardless of whether it's the deposit's most recent. A non-latest active job - left behind
        // by data predating #353/#357, since the index now prevents new ones - is closed immediately
        // by the sweep (PipelineJobCleanupProcessor), not left to block runs indefinitely.
        //
        // Status compared directly (Waiting/Running/MetadataCreated), not
        // PipelineJobStates.IsNotComplete(j.Status): EF Core cannot translate an arbitrary static
        // method call into SQL (see the same note in PipelineJobCleanupProcessor).
        var activeJob = await dbContext.PipelineRunJobs
            .Where(j => j.Deposit == request.DepositId
                        && (j.Status == PipelineJobStates.Waiting
                            || j.Status == PipelineJobStates.Running
                            || j.Status == PipelineJobStates.MetadataCreated))
            .OrderByDescending(j => j.DateSubmitted)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeJob != null)
        {
            return Result.Fail(ErrorCodes.Conflict,
                $"A pipeline run is already in progress for deposit {request.DepositId} " +
                $"(job {activeJob.Id}, {activeJob.Status}). Force complete it first if it is stuck.");
        }

        var callerIdentity = request.User.GetCallerIdentity();
        var (lockFailure, acquiredByThisCall) = await AcquireLock(request.DepositId, callerIdentity, cancellationToken);
        if (lockFailure != null)
        {
            return lockFailure;
        }

        var topicArn = pipelineOptions.Value.PipelineJobTopicArn;
        var jobId = identityMinter.MintIdentity("PipelineJob");

        // Create a new job in the DB
        var newJob = new PipelineRunJob
        {
            DateSubmitted = DateTime.UtcNow,
            ArchivalGroup = entity.ArchivalGroupName,
            PipelineJobJson = JsonSerializer.Serialize(request),
            Id = jobId,
            Status = PipelineJobStates.Waiting,
            Deposit = entity.MintedId,
            LastUpdated = DateTime.UtcNow,
            RunUser = callerIdentity
        };

        try
        {
            dbContext.PipelineRunJobs.Add(newJob);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not create pipeline job row for deposit {DepositId}", request.DepositId);
            if (acquiredByThisCall)
            {
                await ReleaseLockIfHeldBy(request.DepositId, callerIdentity, cancellationToken);
            }
            return Result.Fail(ErrorCodes.UnknownError, "Could not create pipeline job: " + e.Message);
        }

        try
        {
            var pipelineJobMessage = new PipelineJobMessage
            {
                DepositName = request.DepositId,
                JobIdentifier = jobId,
                RunUser = callerIdentity
            };

            await DeliverPipelineJobMessage(pipelineJobMessage, topicArn, request.DepositId, cancellationToken);
        }
        catch (Exception e)
        {
            // The job row exists but Pipeline.API was never told about it, so it will sit as
            // "waiting" forever unless we close it out ourselves here. Identical handling whether
            // delivery was SNS or the local HTTP path above.
            logger.LogError(e, "Could not deliver pipeline job {JobId} for deposit {DepositId}", jobId, request.DepositId);
            newJob.Status = PipelineJobStates.CompletedWithErrors;
            newJob.DateFinished = DateTime.UtcNow;
            newJob.Errors = "Could not queue pipeline job: " + e.Message;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (acquiredByThisCall)
            {
                await ReleaseLockIfHeldBy(request.DepositId, callerIdentity, cancellationToken);
            }
            return Result.Fail(ErrorCodes.UnknownError, "Could not queue pipeline job: " + e.Message);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Extracted out of Handle (Sonar: Cognitive Complexity) - also the natural seam, since this is
    /// the one piece of Handle with a real decision in it: where the job goes. Throws on a local
    /// delivery failure rather than returning a Result, so Handle's existing catch block (which
    /// already marks the job completedWithErrors and releases the lock) handles it identically to a
    /// failed SNS publish, without Handle needing to know which path was taken.
    /// </summary>
    private async Task DeliverPipelineJobMessage(
        PipelineJobMessage pipelineJobMessage, string topicArn, string depositId, CancellationToken cancellationToken)
    {
        if (pipelineDeliveryFlags.Value.UseLocalPipelineDelivery)
        {
            // Local development/testing only (issue #352): deliver directly to the Pipeline API's
            // internal enqueue endpoint over HTTP instead of publishing to SNS, mirroring how
            // imports already work locally. IPipelineJobDeliveryClient is registered only when this
            // flag is on (see Program.cs), so this should never be null here - but if config drifts
            // (the flag on, the client registration somehow missing) a clear exception is far more
            // debuggable than the "Object reference not set..." a bare null-forgiving operator would
            // have produced in the job's recorded error (issue #352 review).
            if (pipelineJobDeliveryClient is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(PipelineDeliveryFlags.UseLocalPipelineDelivery)} is on, but no {nameof(IPipelineJobDeliveryClient)} is registered.");
            }
            var deliveryResult = await pipelineJobDeliveryClient.EnqueuePipelineJob(pipelineJobMessage, cancellationToken);
            if (deliveryResult.Failure)
            {
                throw new InvalidOperationException(
                    deliveryResult.ErrorMessage ?? "The Pipeline API refused the local delivery enqueue call");
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Delivered pipeline job {JobId} for {Identifier} to the Pipeline API directly (local delivery)",
                    pipelineJobMessage.JobIdentifier, depositId);
            }
        }
        else
        {
            // publish the Pipeline job message for Pipeline.API to pick up
            var pubRequest = new PublishRequest(topicArn, JsonSerializer.Serialize(pipelineJobMessage));
            var response = await snsClient.PublishAsync(pubRequest, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
                    response.HttpStatusCode, depositId, response.MessageId);
            }
        }
    }

    private bool IsInDefaultWorkingBucket(Uri? depositFiles) =>
        depositFiles != null
        && AmazonS3Uri.TryParseAmazonS3Uri(depositFiles, out var s3Uri)
        && s3Uri.Bucket == storageOptions.Value.DefaultWorkingBucket;

    /// <summary>
    /// Takes the deposit's lock for the caller, atomically. Null failure means the caller may
    /// proceed, either because this call took the lock or because the caller already held it -
    /// transferred to the run, per issue #299's decision (Tom, 2026-09-23): the run's identity
    /// is the caller's, so no data change is needed, and releasing at the end is exactly the
    /// same operation either way. acquiredByThisCall is true only when this call itself took a
    /// previously-unlocked deposit, so only this call is responsible for releasing it if
    /// something later in Handle fails.
    /// </summary>
    private async Task<(Result? Failure, bool AcquiredByThisCall)> AcquireLock(
        string depositId, string callerIdentity, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // A single conditional UPDATE, so two callers racing for an unlocked deposit cannot both
        // read "unlocked" and both proceed - the same pattern as RunPipelineStatusHandler.ClaimJob.
        var acquired = await dbContext.Deposits
            .Where(d => d.MintedId == depositId && d.LockedBy == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LockedBy, callerIdentity)
                .SetProperty(d => d.LockDate, now), cancellationToken);

        if (acquired > 0)
        {
            return (null, true);
        }

        var currentLockHolder = await dbContext.Deposits
            .Where(d => d.MintedId == depositId)
            .Select(d => d.LockedBy)
            .SingleAsync(cancellationToken);

        if (currentLockHolder == callerIdentity)
        {
            return (null, false);
        }

        return (Result.Fail(ErrorCodes.Conflict,
            $"Could not run pipeline because the deposit {depositId} is locked by " + currentLockHolder), false);
    }

    private async Task ReleaseLockIfHeldBy(string depositId, string callerIdentity, CancellationToken cancellationToken)
    {
        await dbContext.Deposits
            .Where(d => d.MintedId == depositId && d.LockedBy == callerIdentity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LockedBy, (string?)null)
                .SetProperty(d => d.LockDate, (DateTime?)null), cancellationToken);
    }
}

//TODO: put job id into the pipeline into the class
// NB this is the same class as Pipeline.API.Features.Pipeline.PipelineJobMessage
// Public (not internal): IPipelineJobDeliveryClient.EnqueuePipelineJob takes one (issue #352).
public class PipelineJobMessage
{
    [JsonPropertyName("depositname")]
    public required string DepositName { get; set; }

    [JsonPropertyName("jobidentifier")]
    public string? JobIdentifier { get; set; }

    [JsonPropertyName("runuser")]
    public string? RunUser { get; set; }

    public string Type => "PipelineJobMessage";
}