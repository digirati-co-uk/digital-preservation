namespace Storage.API.Features.Import;

/// <summary>
/// Configuration for import job execution, bound once (in Storage.API.Infrastructure.ServiceCollectionX.AddStorageCore)
/// instead of read as raw keys, with their defaults repeated, in up to two places each
/// (<see cref="ImportJobRunner"/>, <see cref="Data.ImportJobResultStore"/>, <see cref="ImportJobReapService"/>
/// and the old <c>ImportJobHeartbeatSettings</c> all carried their own copy of a default - issue #388).
/// </summary>
public class ImportJobOptions
{
    public const string SectionName = "ImportJobs";

    /// <summary>
    /// How often the heartbeat loop writes, not how long a missed heartbeat is tolerated for (that's
    /// <see cref="HeartbeatWindowMinutes"/>) - deliberately two separate knobs. Whichever host
    /// actually runs <see cref="ImportJobRunner"/> - the Storage API in in-process mode, or the
    /// separate SQS Importer in deployed environments - reads this from its own configuration.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// A running job (one that has sent at least one heartbeat) whose heartbeat is older than this is
    /// treated as abandoned. Checked by <see cref="Data.ImportJobResultStore"/>, which always runs in
    /// the Storage API regardless of which host executes jobs.
    /// </summary>
    public int HeartbeatWindowMinutes { get; set; } = 10;

    /// <summary>
    /// How often <see cref="ImportJobReapService"/> sweeps for stale running jobs across every
    /// Archival Group, not just one a caller happens to be touching right now.
    /// </summary>
    public int ReapSweepIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// If the interval were ever configured at or above the window, a live job's own gaps between
    /// heartbeats could be mistaken for abandonment - checked at startup (via ValidateOnStart, where
    /// this is registered) rather than discovered later as jobs failing inexplicably mid-run (issue
    /// #354 review: the Storage API checking only its own two settings missed a misconfigured
    /// importer interval in SQS mode - now both hosts validate the same bound options, unconditionally
    /// regardless of which one actually executes jobs, the same as the static Validate it replaces did).
    /// </summary>
    public bool HeartbeatIntervalIsSafelyBelowWindow() => HeartbeatIntervalSeconds < HeartbeatWindowMinutes * 60;
}
