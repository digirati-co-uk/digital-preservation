namespace Storage.API.Features.Import;

/// <summary>
/// Periodic sweep that reaps any import job whose heartbeat has gone stale, across every Archival
/// Group. GetActiveJobsForArchivalGroup alone only reaps as a side effect of another job being
/// queued for the *same* group - for a group whose only job was simply abandoned (a crashed or
/// killed processor, an ECS task restart), nothing ever asks about that group again, so it would
/// stay blocked until a human deactivates the deposit and starts a new one (issue #354 review).
/// Modelled on Preservation API's PipelineJobCleanupService/StorageImportJobsService.
/// </summary>
public class ImportJobReapService(
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration,
    ILogger<ImportJobReapService> logger) : BackgroundService
{
    private TimeSpan SweepInterval =>
        TimeSpan.FromMinutes(configuration.GetValue("ImportJobs:ReapSweepIntervalMinutes", 5));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"Starting {nameof(ImportJobReapService)}");

        using PeriodicTimer timer = new(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var scope = serviceScopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IImportJobResultStore>();
                var result = await store.ReapAllStaleRunningJobs(stoppingToken);
                if (result.Failure)
                {
                    logger.LogError("Import job reap sweep failed: {Message}", result.ErrorMessage);
                }
                else if (result.Value > 0)
                {
                    logger.LogWarning("Import job reap sweep reaped {Count} stale job(s)", result.Value);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation($"Stopping {nameof(ImportJobReapService)}");
        }
    }
}
