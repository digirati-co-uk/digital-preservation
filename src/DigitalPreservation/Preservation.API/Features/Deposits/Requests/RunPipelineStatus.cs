using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.PipelineApi;
using Preservation.API.Data.Entities;

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipelineStatus(string? id, string status, ClaimsPrincipal user) : IRequest<Result>
{
    public readonly ClaimsPrincipal User = user;
    public string? Id { get; } = id;
    public string Status { get; set; } = status;
}

public class RunPipelineStatusHandler(
    ILogger<RunPipelineStatusHandler> logger,
    PreservationContext dbContext,
    IAmazonSimpleNotificationService snsClient,
    IOptions<PipelineOptions> pipelineOptions) : IRequestHandler<RunPipelineStatus, Result>
{
    public async Task<Result> Handle(RunPipelineStatus request, CancellationToken cancellationToken)
    {
        var deposit = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
        if (deposit == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for ID " + request.Id);
        }

        //TODO: log with Context

        //add entity
        var entity = new PipelineRunJob
        {
            PipelineJobResultId = new Uri("https://test", UriKind.Absolute),
            ArchivalGroup = new Uri("https://test", UriKind.Absolute),
            PipelineJobJson = "",
            //StorageImportJobResultId = storageImportJobResult.Id!,
            Id = Guid.NewGuid().ToString(), //deposit.MintedId + "_" + request.Status
            //ImportJobJson = JsonSerializer.Serialize(request.DepositName),
            Status = request.Status,
            Deposit = deposit.MintedId,
            LastUpdated = DateTime.UtcNow,
            DateSubmitted = DateTime.UtcNow,
            SourceVersion = "",//storageImportJobResult.SourceVersion,
                               //LatestStorageApiResultJson = JsonSerializer.Serialize(storageImportJobResult),
            LatestPreservationApiResultJson = ""//JsonSerializer.Serialize(preservationImportJobResult)
        };
        dbContext.PipelineRunJobs.Add(entity);
        logger.LogInformation("Saving Pipeline Job entity " + entity.Id + " to DB");
        await dbContext.SaveChangesAsync(cancellationToken);

        ////var topicArn = pipelineOptions.Value.PipelineJobTopicArn; //"arn:aws:sns:eu-west-1:975050260954:dlip-pres-dev-pipeline-job-runner-topic";
        ////var pipelineJobMessage = JsonSerializer.Serialize(new PipelineJobStatusMessage { DepositName = request.Id });
        ////var pubRequest = new PublishRequest(topicArn, pipelineJobMessage);
        ////var response = await snsClient.PublishAsync(pubRequest, cancellationToken);

        ////logger.LogDebug(
        ////    "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
        ////    response.HttpStatusCode, request.Id, response.MessageId);

        var callerIdentity = request.User.GetCallerIdentity();
        //logger.LogInformation("Locking deposit {id} for user {user}", request.Id, callerIdentity);
        //deposit.LockedBy = callerIdentity;
        //deposit.LockDate = DateTime.UtcNow;
        //await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

internal class PipelineJobStatusMessage
{
    [JsonPropertyName("depositname")]
    public required string DepositName { get; set; }

    public string Type => "PipelineJobMessage";
}