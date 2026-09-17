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
        
        var callerIdentity = request.User.GetCallerIdentity();
        if (entity.LockedBy is not null && entity.LockedBy != callerIdentity)
        {
            return Result.Fail(ErrorCodes.Conflict,
                $"Could not run pipeline because the deposit {request.DepositId} is locked by " + entity.LockedBy);
        }

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
        dbContext.PipelineRunJobs.Add(newJob);
        await dbContext.SaveChangesAsync(cancellationToken);
        
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

        return Result.Ok();
    }

    private bool IsInDefaultWorkingBucket(Uri? depositFiles) =>
        depositFiles != null
        && AmazonS3Uri.TryParseAmazonS3Uri(depositFiles, out var s3Uri)
        && s3Uri.Bucket == storageOptions.Value.DefaultWorkingBucket;
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