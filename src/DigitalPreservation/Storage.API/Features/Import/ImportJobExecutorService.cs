namespace Storage.API.Features.Import;

public class ImportJobExecutorService(
    IServiceScopeFactory serviceScopeFactory,
    IImportJobQueue importJobQueue,
    ILogger<ImportJobExecutorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation($"Starting {nameof(ImportJobExecutorService)}");

        while (!cancellationToken.IsCancellationRequested)
        {
            var transaction = await importJobQueue.DequeueRequest(cancellationToken);
            
            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ImportJobRunner>();
            await processor.Execute(transaction, cancellationToken);
        }
    }
}