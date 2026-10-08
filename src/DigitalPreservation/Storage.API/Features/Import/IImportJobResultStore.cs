using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;

namespace Storage.API.Features.Import;

public interface IImportJobResultStore
{
    public Task<Result<ImportJob?>> GetImportJob(string jobIdentifier, CancellationToken cancellationToken);
    public Task<Result<ImportJobResult?>> GetImportJobResult(string jobIdentifier, CancellationToken cancellationToken);
    public Task<Result> SaveImportJob(string jobIdentifier, ImportJob importJob, CancellationToken cancellationToken);
    public Task<Result> SaveImportJobResult(string jobIdentifier, ImportJobResult importJobResult, bool active, bool ended, CancellationToken cancellationToken);

    /// <summary>
    /// The runner's own final write for a job, once it has reached a terminal state. Conditional:
    /// only applied while the row is still Active, so a job that was reaped while still (apparently)
    /// running cannot have its abandoned-job result silently overwritten by a "completed" result
    /// that turns out to be true after all. Returns false (not a failure) when the condition did not
    /// hold - the caller should keep the reaper's record and just log that this happened.
    /// </summary>
    public Task<Result<bool>> SaveFinalImportJobResult(string jobIdentifier, ImportJobResult importJobResult, CancellationToken cancellationToken);

    /// <summary>
    /// Records that the job identified by <paramref name="jobIdentifier"/> is still being actively
    /// worked on, using the database's own clock rather than the caller's. A no-op (not a failure)
    /// once the job is no longer Active - e.g. it finished, or was reaped, between the runner
    /// starting its wait and this tick firing.
    /// </summary>
    public Task<Result> Heartbeat(string jobIdentifier, CancellationToken cancellationToken);

    /// <summary>
    /// The executor's own "still running" write, made before it touches Fedora. Conditional like
    /// <see cref="SaveFinalImportJobResult"/>: only applied while the row is still Active, so a job
    /// already reaped as abandoned (a stalled heartbeat while the executor was, in fact, still about
    /// to start) cannot flip back to Active and overwrite the reaper's completedWithErrors record.
    /// Returns false (not a failure) when the condition did not hold - the caller must treat that as
    /// a reason to stop before touching Fedora, not just a logging note.
    /// </summary>
    public Task<Result<bool>> SaveRunningImportJobResult(string jobIdentifier, ImportJobResult importJobResult, CancellationToken cancellationToken);

    public Task<Result<List<string>>> GetActiveJobsForArchivalGroup(Uri? archivalGroup, CancellationToken cancellationToken);

    /// <summary>
    /// Periodic sweep (<see cref="ImportJobReapService"/>) across every Active, heartbeating job
    /// regardless of Archival Group - unlike <see cref="GetActiveJobsForArchivalGroup"/>, which only
    /// reaps as a side effect of another job being queued for the *same* group, so a group whose only
    /// job was abandoned would otherwise never be reaped at all (issue #354 review).
    /// </summary>
    public Task<Result<int>> ReapAllStaleRunningJobs(CancellationToken cancellationToken);

    /// <summary>
    /// Every Active job that has never sent a heartbeat (so never started executing) - the local
    /// equivalent of SQS's dead-letter case when <c>FeatureFlags:UseLocalHostedServiceForImport</c>
    /// is true: the in-process queue is an in-memory channel, so anything still waiting when the
    /// Storage API stops cannot be in the newly-created, empty channel on restart, and is lost for
    /// good. Called once at startup in that mode only - never in SQS mode, where a waiting job may
    /// genuinely still be queued.
    /// </summary>
    public Task<Result<int>> FailOrphanedWaitingJobs(string reason, CancellationToken cancellationToken);

    Task<Result<int>> GetTotalImportJobs(CancellationToken cancellationToken);
    Task<Result<List<DigitalPreservation.Common.Model.ChangeDiscovery.Activity>>> GetActivityPageOfResults(int page, int pageSize, CancellationToken cancellationToken);
}