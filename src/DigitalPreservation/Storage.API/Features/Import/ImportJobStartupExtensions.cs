namespace Storage.API.Features.Import;

public static class ImportJobStartupExtensions
{
    /// <summary>
    /// In-process mode's import queue is an in-memory channel: any job still Active and waiting
    /// (never sent a heartbeat, so never actually started) cannot be in the newly-created, empty
    /// channel this process just built - it was lost when the previous process stopped. This is the
    /// in-process equivalent of an SQS message being dead-lettered, and unlike that case we *can*
    /// tell it apart from "still genuinely queued" here, because there is no queue left to still be
    /// in. A no-op when <paramref name="useLocalHostedServiceForImport"/> is false: in SQS mode a
    /// waiting job may really still be queued.
    /// </summary>
    public static async Task FailOrphanedWaitingImportJobsIfInProcessAsync(
        this WebApplication app, bool useLocalHostedServiceForImport)
    {
        if (!useLocalHostedServiceForImport)
        {
            return;
        }

        using var startupScope = app.Services.CreateScope();
        var importJobResultStore = startupScope.ServiceProvider.GetRequiredService<IImportJobResultStore>();
        var orphanedResult = await importJobResultStore.FailOrphanedWaitingJobs(
            "never started: the in-process import queue was lost when the Storage API restarted",
            CancellationToken.None);
        if (orphanedResult.Success && orphanedResult.Value > 0)
        {
            app.Logger.LogWarning("Failed {Count} import job(s) left waiting in the in-process queue by a previous run",
                orphanedResult.Value);
        }
        else if (orphanedResult.Failure)
        {
            app.Logger.LogError("Could not check for orphaned waiting import jobs at startup: {CodeAndMessage}",
                orphanedResult.CodeAndMessage());
        }
    }
}
