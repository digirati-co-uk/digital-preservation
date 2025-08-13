using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Pipeline.API.Aws;

namespace Pipeline.API.Features.Pipeline;

public class SqsPipelineQueue(
    ILogger<SqsPipelineQueue> logger,
    IAmazonSimpleNotificationService snsClient,
    IAmazonSQS sqsClient,
    IOptions<PipelineOptions> options,
    IIdentityMinter identityMinter) : IPipelineQueue
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
        IEnumerable<Subscription>? queues = new List<Subscription>();

        try
        {
            var subs = await snsClient.ListSubscriptionsByTopicAsync(
                new ListSubscriptionsByTopicRequest
                {
                    TopicArn = options.Value.PipelineJobTopicArn
                }, cancellationToken);

            queues = subs.Subscriptions.Where(x => x.Protocol.ToLower() == "sqs");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }

        foreach (var queue in queues)
        {
            var queueNameValue = Arn.Parse(queue.Endpoint).Resource;
            logger.LogInformation($"About to check queue {queueNameValue} for messages");

            try
            {
                var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueNameValue, cancellationToken);
                var queueUrlValue = queueUrlResponse.QueueUrl;

                var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrlValue,
                    WaitTimeSeconds = 10,
                    MaxNumberOfMessages = 1
                }, cancellationToken);

                var messageCount = response.Messages?.Count ?? 0;

                if (messageCount <= 0) continue;

                foreach (var message in response.Messages!)
                {
                    if (cancellationToken.IsCancellationRequested) return messageModel;
                    logger.LogDebug("Received SQS message {messageBody}", message.Body);

                    messageModel = GetMessageModel(message, queueNameValue);
                    if (messageModel.DepositName.HasText())
                    {
                        await DeleteMessage(message, queueUrlValue, cancellationToken);
                        return messageModel;
                    }

                    return messageModel;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in listen loop for queue {Queue}", queueNameValue);
            }

        }

        return messageModel;
    }

    private PipelineJobMessage? GetMessageModel(Message message, string queue)
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
                if (string.IsNullOrEmpty(pipelineJobMessage.JobIdentifier))
                {
                    pipelineJobMessage.JobIdentifier = identityMinter.MintIdentity(nameof(PipelineJob));
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