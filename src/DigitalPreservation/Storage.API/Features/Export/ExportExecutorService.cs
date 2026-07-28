namespace Storage.API.Features.Export;

public class ExportExecutorService(
    IServiceScopeFactory serviceScopeFactory,
    IExportQueue exportQueue,
    ILogger<ExportExecutorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"Starting {nameof(ExportExecutorService)}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var transaction = await exportQueue.DequeueRequest(stoppingToken);

            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ExportRunner>();
            await processor.Execute(transaction, stoppingToken);
        }
    }
}