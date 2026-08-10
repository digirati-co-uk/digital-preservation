using DigitalPreservation.Utils;

namespace Storage.API.Features.Import;

public class ImportJobExecutorService(
    IServiceScopeFactory serviceScopeFactory,
    IImportJobQueue importJobQueue,
    ILogger<ImportJobExecutorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"Starting {nameof(ImportJobExecutorService)}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var transaction = await importJobQueue.DequeueRequest(stoppingToken);
            if (transaction.HasText())
            {
                using var scope = serviceScopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ImportJobRunner>();
                await processor.Execute(transaction, stoppingToken);
            }
        }
    }
}