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
                try
                {
                    await processor.Execute(transaction, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // A BackgroundService that lets an exception escape stops the whole host by
                    // default; one job that throws must not take the import service down with it.
                    logger.LogError(e, "Import job {JobIdentifier} threw; continuing with the next job", transaction);
                }
            }
        }
    }
}