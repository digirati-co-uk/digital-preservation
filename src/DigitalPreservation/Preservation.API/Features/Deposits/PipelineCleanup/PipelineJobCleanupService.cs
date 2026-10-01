namespace Preservation.API.Features.Deposits.PipelineCleanup;

/// <summary>
/// Periodic sweep that closes out stalled pipeline runs, moved here from the Deposit page's GET in
/// the UI (issue #301): a page view is the wrong trigger - it only fires for a deposit somebody
/// happens to open, two people opening it at once both ran the cleanup, and it released whatever
/// lock was on the deposit regardless of who held it. Modelled on StorageImportJobsService.
/// </summary>
public class PipelineJobCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<PipelineJobCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"Starting {nameof(PipelineJobCleanupService)}");

        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var scope = serviceScopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PipelineJobCleanupProcessor>();
                var result = await processor.CleanupOverdueJobs(stoppingToken);
                if (result.Failure)
                {
                    logger.LogError("Pipeline job cleanup sweep failed: {Message}", result.ErrorMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation($"Stopping {nameof(PipelineJobCleanupService)}");
        }
    }
}
