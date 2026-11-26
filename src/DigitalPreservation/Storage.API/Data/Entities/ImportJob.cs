// ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
namespace Storage.API.Data.Entities;

public class ImportJob
{
    public required string Id { get; set; }
    
    public required Uri ArchivalGroup { get; set; }
    
    public required string ImportJobJson { get; set; }
    
    public string? ImportJobResultJson { get; set; }

    public bool Active { get; set; } = true;
    public DateTime? Received { get; set; }
    public DateTime? EndTime { get; set; }

    public Uri? ImportJobResultUri { get; set; }

    /// <summary>
    /// Written by <see cref="Storage.API.Features.Import.ImportJobRunner"/> roughly once a minute
    /// while the job actually runs, using the database's own clock. Null means the job has never
    /// started executing (still queued): <see cref="Storage.API.Features.Import.IImportJobResultStore.GetActiveJobsForArchivalGroup"/>
    /// never reaps a null-heartbeat job by age, since the message may simply still be in the queue.
    /// Once set, a heartbeat older than the configured window means the runner that was writing it
    /// is gone, and the job is reaped.
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// Written by <see cref="Storage.API.Features.Import.Requests.ExecuteImportJobHandler"/>, using the
    /// database's own clock, immediately before it sends the commit (issue #444). From then on Fedora may
    /// finish the commit even if the importer dies, so a stale job with this set is reconciled
    /// (<see cref="Storage.API.Features.Import.ImportJobCommitReconciler"/>) rather than reaped as failed.
    /// The job's result JSON, saved in the same write, carries the source version and the lists of what
    /// was added, patched, renamed and deleted.
    /// </summary>
    public DateTime? CommitStarted { get; set; }

    /// <summary>The Fedora transaction being committed, saved with <see cref="CommitStarted"/>.</summary>
    public Uri? CommitTransaction { get; set; }
}