using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;

namespace Storage.API.Features.Import;

public interface IImportJobResultStore
{
    public Task<Result<ImportJob?>> GetImportJob(string jobIdentifier, CancellationToken cancellationToken);
    public Task<Result<ImportJobResult?>> GetImportJobResult(string jobIdentifier, CancellationToken cancellationToken);
    /// <summary>
    /// Inserts a new, active job. Fails with Conflict if its Archival Group already has an active
    /// job: a database index enforces one per group, so this holds even when two requests race
    /// past the caller's own check (issue #460).
    /// </summary>
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

    /// <summary>
    /// Issue #444: the executor's last write before it sends the commit. Saves the result - with its
    /// source version and the lists of what was added, patched, renamed and deleted, which otherwise
    /// live only in memory - and marks the commit as started, with its transaction, on the database's
    /// clock. From then on the job is reconciled, not reaped, if its heartbeat goes stale. Conditional
    /// on the row still being Active, like <see cref="SaveRunningImportJobResult"/>: false means the job
    /// was reaped first, and the caller must not commit.
    /// </summary>
    public Task<Result<bool>> RecordCommitStarted(string jobIdentifier, ImportJobResult importJobResult, Uri transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Active jobs whose commit had started (<see cref="RecordCommitStarted"/>) and whose heartbeat has
    /// since gone stale: the importer died, or stalled, mid-commit. For <see cref="ImportJobCommitReconciler"/>.
    /// </summary>
    public Task<Result<List<StaleCommittingJob>>> GetStaleCommittingJobs(CancellationToken cancellationToken);

    /// <summary>
    /// The reconciler's final write for a job whose commit was interrupted. Conditional: only while the
    /// row is still Active, the commit is recorded as started, and the heartbeat is still stale - so a
    /// runner that was only paused, and has since written its own result or a fresh heartbeat, wins.
    /// </summary>
    public Task<Result<bool>> SaveReconciledImportJobResult(string jobIdentifier, ImportJobResult importJobResult, CancellationToken cancellationToken);

    /// <summary>
    /// Active jobs for the group. Reaps stale ones as a side effect, except a job whose commit had
    /// started: this runs inside an HTTP request and can't wait for a commit to finish, so that job stays
    /// active, and keeps blocking the group, until the reconciler has decided its outcome (issue #444).
    /// </summary>
    public Task<Result<List<string>>> GetActiveJobsForArchivalGroup(Uri? archivalGroup, CancellationToken cancellationToken);

    /// <summary>
    /// Operator path for issue #366: clears a job that is Active but has never sent a heartbeat at
    /// all - dead-lettered, genuinely still queued (SQS retains a message for up to 14 days), or
    /// mid-run on an importer from before heartbeats existed (#354). That is exactly the gap
    /// <see cref="GetActiveJobsForArchivalGroup"/> and <see cref="ReapAllStaleRunningJobs"/>
    /// deliberately leave alone, because neither can tell those three cases apart on its own. This
    /// is instead a deliberate, named, operator action: the caller is asserting - from evidence
    /// outside this API - that the job will never run, so it is honoured even though a null
    /// heartbeat alone is not proof of that. Refuses a job that already has a heartbeat (that one
    /// belongs to the automatic reap instead) or is already inactive. Replaces the raw
    /// <c>update import_jobs set active = false where id = …</c> SQL that was the only remedy before
    /// this existed (issue #245).
    /// </summary>
    public Task<Result<ImportJobResult>> CancelStuckImportJob(string jobIdentifier, string reason, CancellationToken cancellationToken);

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