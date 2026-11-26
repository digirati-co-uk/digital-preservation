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

    /// <summary>
    /// <b>For running the pipeline on a developer's own machine only.</b> Copy every deposit from S3
    /// to <see cref="ScratchCopyPath"/>, as for a caller's bucket, instead of reading the default
    /// working bucket through <see cref="FileMountPath"/>: there is no S3 mount on a laptop, and
    /// copying a deposit into a folder standing in for one by hand is tedious. Honoured only when
    /// the environment is <c>Development</c>, which is what running from an IDE or <c>dotnet run</c>
    /// gives; anywhere else (the deployed DEV, TEST and PROD environments included) it is ignored
    /// with a warning, since they have the mount and copying would only cost time and disk.
    /// </summary>
    public bool CopyEveryDepositToScratch { get; set; }
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
