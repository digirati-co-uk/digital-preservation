namespace Preservation.API.Features.Activity.Readers;

public class StorageImportJobsService(
    IServiceScopeFactory serviceScopeFactory, 
    ILogger<StorageImportJobsService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation($"Starting {nameof(StorageImportJobsService)}");

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<StorageImportJobsProcessor>();
            var result = await processor.ReadStream(cancellationToken);
            if (result.Success)
            {
                logger.LogInformation("Completed read of Storage API Import Jobs Activity Stream; waiting 1 minute...");
                Thread.Sleep(1000 * 60); 
            }
            else
            {
                logger.LogError("FAILED read of Storage API Import Jobs Activity Stream, waiting 30 minutes: {message}", result.ErrorMessage);
                Thread.Sleep(1000 * 60 * 30);
            }
        }
    }
}