namespace Preservation.API.Features.Activity.Readers;

public class StorageImportJobsService(
    IServiceScopeFactory serviceScopeFactory, 
    ILogger<StorageImportJobsService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation($"Starting {nameof(StorageImportJobsService)}");
        
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(60));
        var delay = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (delay > 0)
                {
                    logger.LogInformation("Skipping read of storage API, delay {delay} minutes", delay);
                    delay--;
                    continue;
                }
                using var scope = serviceScopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<StorageImportJobsProcessor>();
                var result = await processor.ReadStream(cancellationToken);
                if (result.Success)
                {
                    logger.LogInformation("Completed read of Storage API Import Jobs Activity Stream; waiting 1 minute...");
                    delay = 0;
                }
                else
                {
                    logger.LogError("FAILED read of Storage API Import Jobs Activity Stream, waiting 30 minutes: {message}", result.ErrorMessage);
                    delay = 30;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation($"Stopping {nameof(StorageImportJobsService)}");
        }
    }
}