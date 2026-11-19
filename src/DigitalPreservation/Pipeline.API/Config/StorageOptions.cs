namespace Pipeline.API.Config;

public class StorageOptions
{
    /// <summary>
    /// Where the default working bucket is mounted as a filesystem (AWS Mountpoint, set up in the
    /// EC2 cluster's user data). Deposits in that bucket are read in place from here.
    /// </summary>
    public string? FileMountPath { get; set; }

    /// <summary>
    /// Local directory for scratch copies of deposits in a caller's own bucket, which is not
    /// mounted (issue #410). Each job's copy is a subdirectory, deleted when the job ends.
    /// </summary>
    public string? ScratchCopyPath { get; set; }

    /// <summary>
    /// The largest deposit, in bytes, that will be copied to <see cref="ScratchCopyPath"/>. A larger
    /// one is refused before anything is downloaded, rather than sizing the disk for the worst case.
    /// </summary>
    public long ScratchCopyMaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}

/// <summary>
/// Bound from the FeatureFlags section.
/// </summary>
public class PipelineFeatureFlags
{
    /// <summary>
    /// Copy a deposit in a caller's own bucket to <see cref="StorageOptions.ScratchCopyPath"/> and run
    /// the pipeline against the copy (issue #410). Off by default; switched on per environment
    /// together with the Preservation API's flag of the same name, which lets such jobs be queued.
    /// </summary>
    public bool AllowPipelineForCallerBuckets { get; set; }
}
