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

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipeline(string id, ClaimsPrincipal user, string? runUser, string? jobId) : IRequest<Result>
{
    public readonly ClaimsPrincipal User = user;
    public string Id { get; } = id;
    public string? JobId { get; set; } = jobId;
    public string? RunUser { get; set; } = runUser;

}

public class RunPipelineHandler(
    ILogger<RunPipelineHandler> logger,
    PreservationContext dbContext,
    IAmazonSimpleNotificationService snsClient,
    IOptions<PipelineOptions> pipelineOptions) : IRequestHandler<RunPipeline, Result>
{
    public async Task<Result> Handle(RunPipeline request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);

        if (entity == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "Could not run pipeline because could not find no deposit for ID " + request.Id);
        }

        if (entity.LockedBy is not null && entity.LockedBy != request.RunUser)
        {
            return Result.Fail(ErrorCodes.Conflict, $"Could not run pipeline because the deposit {request.Id} is locked by " + entity.LockedBy);
        }

        var topicArn = pipelineOptions.Value.PipelineJobTopicArn;

        var pipelineJobMessage = JsonSerializer.Serialize(new PipelineJobMessage { DepositName = request.Id, JobIdentifier = request.JobId, RunUser = request.RunUser});
        var pubRequest = new PublishRequest(topicArn, pipelineJobMessage);

        var response = await snsClient.PublishAsync(pubRequest, cancellationToken);

        logger.LogDebug(
            "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
            response.HttpStatusCode, request.Id, response.MessageId);

        return Result.Ok();
    }
}

//TODO: put job id into the pipeline into the class
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