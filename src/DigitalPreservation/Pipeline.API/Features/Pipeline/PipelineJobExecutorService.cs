using DigitalPreservation.Utils;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobExecutorService(
    IServiceScopeFactory serviceScopeFactory,
    IPipelineQueue pipelineQueue,
    ILogger<PipelineJobExecutorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation($"Starting {nameof(PipelineJobExecutorService)}");

        while (!cancellationToken.IsCancellationRequested)
        {
            var transaction = await pipelineQueue.DequeueRequest(cancellationToken); //get deposit name out of message then delete message
            if (!transaction.HasText()) continue; //dont have deposit name
            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<PipelineJobRunner>();
            await processor.Execute(transaction, cancellationToken); //passing deposit name
        }
    }
}