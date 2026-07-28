using DigitalPreservation.Utils;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobExecutorService(
    IServiceScopeFactory serviceScopeFactory,
    IPipelineQueue pipelineQueue,
    ILogger<PipelineJobExecutorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"Starting {nameof(PipelineJobExecutorService)}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var transaction = await pipelineQueue.DequeueRequest(stoppingToken);
            if (transaction == null || !transaction.DepositName.HasText()) continue;
            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<PipelineJobRunner>();

            logger.LogInformation("About to execute the pipeline run for deposit {Deposit}", transaction.DepositName);
            await processor.Execute(transaction, stoppingToken);
        }
    }
}