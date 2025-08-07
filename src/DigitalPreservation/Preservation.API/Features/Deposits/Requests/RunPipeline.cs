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

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipeline(string id, ClaimsPrincipal user) : IRequest<Result>
{
    public readonly ClaimsPrincipal User = user;
    public string Id { get; } = id;
}

//TODO: UI uses this
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
            return Result.Fail(ErrorCodes.NotFound, "No deposit for ID " + request.Id);
        }

        var topicArn = pipelineOptions.Value.PipelineJobTopicArn;
        var pipelineJobMessage = JsonSerializer.Serialize(new PipelineJobMessage { DepositName = request.Id });
        var pubRequest = new PublishRequest(topicArn, pipelineJobMessage);
        var response = await snsClient.PublishAsync(pubRequest, cancellationToken);
        logger.LogDebug(
            "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
            response.HttpStatusCode, request.Id, response.MessageId);

        return Result.Ok();
    }
}

internal class PipelineJobMessage
{
    [JsonPropertyName("depositname")]
    public required string DepositName { get; set; }

    public string Type => "PipelineJobMessage";
}