using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Storage.Repository.Common.Aws;

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

    private async Task EnsureOptions(CancellationToken cancellationToken)
    {
        if (queueUrl == null)
        {
            queueName = options.Value.ImportJobSqsQueueName;
            var result = await sqsClient.GetQueueUrlAsync(queueName, cancellationToken);
            queueUrl = result.QueueUrl;
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
        string jobIdentifier = string.Empty;
        try
        {
            // WaitTimeSeconds below is an SQS-side long-poll duration, not a client-side timeout - if
            // the underlying connection to SQS goes dead without erroring, these calls can hang
            // indefinitely with no exception, silently wedging this single-threaded consumer loop
            // forever (the failure class behind LPII-135, fixed the same way in SqsPipelineQueue).
            // Bound the whole dequeue attempt so a hang degrades to a retried failure on the next
            // loop iteration instead of a permanent stall.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var linkedToken = linkedCts.Token;

            await EnsureOptions(linkedToken);

            var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                WaitTimeSeconds = 10,
                MaxNumberOfMessages = 1
            }, linkedToken);

            foreach (var message in response.Messages ?? [])
            {
                if (cancellationToken.IsCancellationRequested) return string.Empty;
                logger.LogDebug("Received SQS message {MessageBody}", message.Body);

                jobIdentifier = GetJobId(message);
                if (jobIdentifier.HasText())
                {
                    // Deliberately not scoped to the 30s dequeue-attempt timeout above: cancelling a
                    // delete mid-flight would leave the message undeleted, so it reappears after the
                    // visibility timeout and the job runs a second time.
                    await DeleteMessage(message, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            // Also catches OperationCanceledException from the 30s bound; the consumer loop in
            // ImportJobExecutorService has no catch of its own, so before this guard any receive
            // error stopped the BackgroundService outright.
            logger.LogError(ex, "Error in dequeue attempt for queue {Queue}", queueName);
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