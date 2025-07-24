using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using Pipeline.API.Aws;

namespace Pipeline.API.Features.Pipeline;

public class SqsPipelineQueue(
    ILogger<SqsPipelineQueue> logger,
    IAmazonSimpleNotificationService snsClient,
    IAmazonSQS sqsClient,
    IOptions<PipelineOptions> options) : IPipelineQueue
{
    private string? topicArn;

    /// this is used for running locally until I get me Leeds AWS sorted then it will be removed
    private void GetCredentials()
    {
        string profilePath1 = "C:\\Users\\BrianMcEnroy\\.aws\\credentials";
        CredentialProfile basicProfile;
        var sharedFile = new SharedCredentialsFile(profilePath1);

        var s = sharedFile.TryGetProfile("uol-bm", out basicProfile);
        var a = AWSCredentialsFactory.TryGetAWSCredentials(basicProfile, sharedFile, out _);

        if (sharedFile.TryGetProfile("uol-bm", out basicProfile) &&
            AWSCredentialsFactory.TryGetAWSCredentials(basicProfile, sharedFile, out _))
        {
            sharedFile.TryGetProfile("uol-bm", out var profile);
            AWSCredentialsFactory.TryGetAWSCredentials(profile, sharedFile, out var credentials);

            sqsClient = new AmazonSQSClient(credentials, basicProfile.Region);
            snsClient = new AmazonSimpleNotificationServiceClient(credentials, basicProfile.Region);
        }
    }

    public async ValueTask QueueRequest(string depositName, CancellationToken cancellationToken)
    {
        topicArn = options.Value.PipelineJobTopicArn;
        var pipelineJobMessage = JsonSerializer.Serialize(new PipelineJobMessage { DepositName = depositName});
        var request = new PublishRequest(topicArn, pipelineJobMessage);
        var response = await snsClient.PublishAsync(request, cancellationToken);
        logger.LogDebug(
            "Received statusCode {StatusCode} for sending to SNS for {Identifier} - {MessageId}",
            response.HttpStatusCode, depositName, response.MessageId);
    }

    public async ValueTask<string> DequeueRequest(CancellationToken cancellationToken)
    {
        // below method GetCredentials(); call is used for running locally until I get me Leeds AWS sorted then it will be removed
        //allows me to run the SDK
        //GetCredentials();
        var depositName = string.Empty;

        //TODO: get first message out of all the queues in subscription then exit
        var subs = await snsClient.ListSubscriptionsByTopicAsync(
            new ListSubscriptionsByTopicRequest
            {
                TopicArn = options.Value.PipelineJobTopicArn
            }, cancellationToken);

        var queues = subs.Subscriptions.Where(x => x.Protocol.ToLower() == "sqs");

        foreach (var queue in queues)
        {
            var queueNameValue = Arn.Parse(queue.Endpoint).Resource;

            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueNameValue, cancellationToken); 
            var queueUrlValue = queueUrlResponse.QueueUrl;

            var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrlValue,
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

                        depositName = GetDepositName(message, queueNameValue);
                        if (depositName.HasText())
                        {
                            await DeleteMessage(message, queueUrlValue, cancellationToken);
                            return depositName;
                        }

                        return string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in listen loop for queue {Queue}", queueNameValue);
                }
            }

        }

        return depositName;
    }

    private string GetDepositName(Message message, string queue)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Handling message {Message} from {Queue}", message.MessageId, queue);
            }

            var queueMessage = QueueMessage.FromSqsMessage(message, queue!);
            var importJobMessage = queueMessage.GetMessageContents<PipelineJobMessage>();
            if (importJobMessage != null)
            {
                return importJobMessage.DepositName;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message {MessageId} from queue {Queue}",
                message.MessageId, queue);
        }
        return string.Empty;
    }


    private Task DeleteMessage(Message message, string queueUrl, CancellationToken cancellationToken)
        => sqsClient.DeleteMessageAsync(new DeleteMessageRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = message.ReceiptHandle
        }, cancellationToken);
}


public class PipelineOptions
{
    public const string PipelineJob = "PipelineJob";
    public required string PipelineJobTopicArn { get; set; }
    public required string PipelineJobSqsQueueName { get; set; }
}

internal class PipelineJobMessage
{
    [JsonPropertyName("depositname")]
    public required string DepositName { get; set; }
    
    public string Type => "PipelineJobMessage";
} 