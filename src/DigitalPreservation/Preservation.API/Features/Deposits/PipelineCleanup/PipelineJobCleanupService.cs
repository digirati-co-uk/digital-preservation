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

        // A job overdue by DateBegun/DateSubmitted is only overdue after PipelineJobsCleanupMinutes
        // (a day by default), but a non-latest active job is now closed on the very next tick
        // regardless of that cutoff (issue #357) - so this 15-minute interval is how long such a job
        // lingers, blocking RunPipelineHandler and sitting unreachable in the UI, not a value that
        // "buys nothing" between ticks the way it used to read (issue #357 review nit).
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(15));
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
