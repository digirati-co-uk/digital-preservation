using System.Text.Json.Nodes;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Storage.API.Aws;

namespace Storage.API.Features.Import;

public class SqsImportJobQueue(
    ILogger<SqsImportJobQueue> logger,
    IAmazonSQS sqsClient,
    IOptions<ImportOptions> options) : IImportJobQueue
{
    private string? queueUrl;
    private string? queueName;

    private async Task EnsureQueueUrl()
    {
        if (queueUrl == null)
        {
            queueName = options.Value.ImportJobSqsQueueName;
            try
            {
                var result = await sqsClient.GetQueueUrlAsync(queueName);
                queueUrl = result.QueueUrl;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not resolve queue name {queueName}", queueName);
            }
        }
    }
    public async ValueTask QueueRequest(string jobIdentifier, CancellationToken cancellationToken)
    {
        await EnsureQueueUrl();
        var request = new SendMessageRequest(queueUrl, jobIdentifier);
        var response = await sqsClient.SendMessageAsync(request, cancellationToken);
        logger.LogDebug(
            "Received statusCode {StatusCode} for sending SQS for {Identifier} - {MessageId}",
            response.HttpStatusCode, jobIdentifier, response.MessageId);
    }

    public async ValueTask<string> DequeueRequest(CancellationToken cancellationToken)
    {
        await EnsureQueueUrl();
        string jobIdentifier = string.Empty;
        var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            WaitTimeSeconds = 10,
            MaxNumberOfMessages = 1
        }, cancellationToken);
        var messageCount = response.Messages?.Count ?? 0;
        if (messageCount > 0)
        {
            try
            {
                foreach (var message in response.Messages!)
                {
                    if (cancellationToken.IsCancellationRequested) return string.Empty;
                    logger.LogDebug("Received SQS message {messageBody}", message.Body);

                    jobIdentifier = GetJobId(message);
                    if (jobIdentifier.HasText())
                    {
                        await DeleteMessage(message, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in listen loop for queue {Queue}", queueName);
            }
        }

        return jobIdentifier;
    }

    private string GetJobId(Message message)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Handling message {Message} from {Queue}", message.MessageId, queueName);
            }

            var messageAttributes = message.MessageAttributes
                .ToDictionary(pair => pair.Key, pair => pair.Value.StringValue);

            var queueMessage = new QueueMessage
            {
                MessageAttributes = messageAttributes,
                Attributes = message.Attributes,
                Body = JsonNode.Parse(message.Body)!.AsObject(),
                MessageId = message.MessageId,
                QueueName = queueName!
            };

            return queueMessage.GetMessageContents<string>() ?? string.Empty;

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message {MessageId} from queue {Queue}",
                message.MessageId, queueName);
        }
        return string.Empty;
    }
    
    
    private Task DeleteMessage(Message message, CancellationToken cancellationToken)
        => sqsClient.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = message.ReceiptHandle
        }, cancellationToken);
}


public class ImportOptions
{
    public required string ImportJobSqsQueueName { get; set; }
}