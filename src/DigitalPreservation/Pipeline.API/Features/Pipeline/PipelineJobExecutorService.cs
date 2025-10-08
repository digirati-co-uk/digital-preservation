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
            var transaction = await pipelineQueue.DequeueRequest(cancellationToken); 
            if (transaction == null || !transaction.DepositName.HasText()) continue; 
            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<PipelineJobRunner>();

            logger.LogInformation("About to execute the pipeline run for deposit {deposit}", transaction.DepositName);
            await processor.Execute(transaction, cancellationToken);
        }
    }
}