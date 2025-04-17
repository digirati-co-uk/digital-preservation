using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Storage.API.Aws;

namespace Storage.API.Features.Import;

public class SqsImportJobQueue(
    ILogger<SqsImportJobQueue> logger,
    IAmazonSimpleNotificationService simpleNotificationService,
    IAmazonSQS sqsClient,
    IOptions<ImportOptions> options) : IImportJobQueue
{
    private string? queueUrl;
    private string? queueName;
    private string? topicArn;

    private async Task EnsureOptions()
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
        topicArn = options.Value.ImportJobTopicArn;
        var importJobMessage = JsonSerializer.Serialize(new ImportJobMessage { Id = jobIdentifier });
        var request = new PublishRequest(topicArn, importJobMessage);
        var response = await simpleNotificationService.PublishAsync(request, cancellationToken);
        logger.LogDebug(
            "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
            response.HttpStatusCode, jobIdentifier, response.MessageId);
    }

    public async ValueTask<string> DequeueRequest(CancellationToken cancellationToken)
    {
        await EnsureOptions();
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

            var queueMessage = QueueMessage.FromSqsMessage(message, queueName!);
            var importJobMessage = queueMessage.GetMessageContents<ImportJobMessage>();
            if (importJobMessage != null)
            {
                return importJobMessage.Id;
            }
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
    public const string ImportExport = "ImportExport";
    public required string ImportJobTopicArn { get; set; }
    public required string ImportJobSqsQueueName { get; set; }
    public required string ExportJobTopicArn { get; set; }
    public required string ExportJobSqsQueueName { get; set; }
}

internal class ImportJobMessage
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    
    public string Type => "ImportJobMessage";
} 