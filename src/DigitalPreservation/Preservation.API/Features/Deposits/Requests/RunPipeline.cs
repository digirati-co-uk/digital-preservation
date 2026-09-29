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
    IIdentityMinter identityMinter) : IRequestHandler<RunPipeline, Result>
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
            // publish the Pipeline job message for Pipeline.API to pick up
            var pipelineJobMessage = JsonSerializer.Serialize(new PipelineJobMessage
            {
                DepositName = request.DepositId,
                JobIdentifier = jobId,
                RunUser = callerIdentity
            });
            var pubRequest = new PublishRequest(topicArn, pipelineJobMessage);
            var response = await snsClient.PublishAsync(pubRequest, cancellationToken);

            logger.LogDebug(
                "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
                response.HttpStatusCode, request.DepositId, response.MessageId);
        }
        catch (Exception e)
        {
            // The job row exists but Pipeline.API was never told about it, so it will sit as
            // "waiting" forever unless we close it out ourselves here.
            logger.LogError(e, "Could not publish pipeline job {JobId} for deposit {DepositId} to SNS", jobId, request.DepositId);
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
internal class PipelineJobMessage
{
    [JsonPropertyName("depositname")]
    public required string DepositName { get; set; }

    [JsonPropertyName("jobidentifier")]
    public string? JobIdentifier { get; set; }

    [JsonPropertyName("runuser")]
    public string? RunUser { get; set; }

    public string Type => "PipelineJobMessage";
}