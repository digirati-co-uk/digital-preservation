using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Preservation.Client;
using Storage.Repository.Common.Aws;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pipeline.API.Features.Pipeline;

public class SqsPipelineQueue(
    ILogger<SqsPipelineQueue> logger,
    IAmazonSimpleNotificationService snsClient,
    IAmazonSQS sqsClient,
    IOptions<PipelineOptions> options,
    IIdentityMinter identityMinter,
    IPreservationApiClient preservationApiClient) : IPipelineQueue
{
    private string? topicArn;

    public async ValueTask QueueRequest(string jobIdentifier, string depositName, string? runUser, CancellationToken cancellationToken)
    {
        topicArn = options.Value.PipelineJobTopicArn;
        var pipelineJobMessage = JsonSerializer.Serialize(new PipelineJobMessage { JobIdentifier = jobIdentifier, DepositName = depositName, RunUser = runUser});
        var request = new PublishRequest(topicArn, pipelineJobMessage);
        var response = await snsClient.PublishAsync(request, cancellationToken);
        logger.LogDebug(
            "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
            response.HttpStatusCode, depositName, response.MessageId);
    }

    public async ValueTask<PipelineJobMessage?> DequeueRequest(CancellationToken cancellationToken) 
    {
        PipelineJobMessage? messageModel = null;

        var queue = options.Value.PipelineJobQueue;
        logger.LogInformation($"About to check queue {queue} for messages");
        var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queue, cancellationToken);
        var queueUrlValue = queueUrlResponse.QueueUrl;

        var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrlValue,
            WaitTimeSeconds = 10,
            MaxNumberOfMessages = 1
        }, cancellationToken);

        foreach (var message in response.Messages!)
        {
            if (cancellationToken.IsCancellationRequested) return messageModel;
            logger.LogDebug("Received SQS message {messageBody}", message.Body);

            messageModel = await GetMessageModel(message, queue);
            if (messageModel != null && !messageModel.DepositName.HasText()) 
                return messageModel;

            await DeleteMessage(message, queueUrlValue, cancellationToken);
            return messageModel;

        }

        return messageModel;
    }

    private async Task<PipelineJobMessage?> GetMessageModel(Message message, string queue)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Handling message {Message} from {Queue}", message.MessageId, queue);
            }

            var queueMessage = QueueMessage.FromSqsMessage(message, queue!);
            var pipelineJobMessage = queueMessage.GetMessageContents<PipelineJobMessage>();
            if (pipelineJobMessage != null)
            {
                var depositPipelineResults = await preservationApiClient.GetPipelineJobResultsForDeposit(pipelineJobMessage.DepositName, new CancellationToken());
                
                if (string.IsNullOrEmpty(pipelineJobMessage.JobIdentifier))
                {
                    pipelineJobMessage.JobIdentifier = identityMinter.MintIdentity("PipelineJob");
                }

                if (depositPipelineResults.Value != null)
                {
                    var job = depositPipelineResults.Value.FirstOrDefault(x => x.JobId == pipelineJobMessage.JobIdentifier && x.Status == PipelineJobStates.CompletedWithErrors);
                    if (job != null)
                    {
                        //job has been forced to complete
                        return null;
                    }
                }

                return pipelineJobMessage;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message {MessageId} from queue {Queue}",
                message.MessageId, queue);
        }
        return null;
    }


    private Task DeleteMessage(Message message, string queueUrl, CancellationToken cancellationToken)
        => sqsClient.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = message.ReceiptHandle
        }, cancellationToken);
}


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