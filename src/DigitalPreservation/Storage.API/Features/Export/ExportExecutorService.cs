namespace Storage.API.Features.Export;
using ExportResource = DigitalPreservation.Common.Model.Export.Export;


public class ExportExecutorService(
    IServiceScopeFactory serviceScopeFactory,
    IExportQueue exportQueue,
    ILogger<ExportExecutorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation($"Starting {nameof(ExportExecutorService)}");

        while (!cancellationToken.IsCancellationRequested)
        {
            var transaction = await exportQueue.DequeueRequest(cancellationToken);
            
            using var scope = serviceScopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ExportRunner>();
            await processor.Execute(transaction, cancellationToken);
        }
    }
}