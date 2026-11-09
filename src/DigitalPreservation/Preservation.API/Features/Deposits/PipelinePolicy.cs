using DigitalPreservation.Core.Auth;

namespace Preservation.API.Features.Deposits;

/// <summary>Whether deposits in a bucket may run the pipeline, and if not, why (issue #410).</summary>
public enum PipelineAccess
{
    Permitted,

    /// <summary>A caller's own bucket, while AllowPipelineForCallerBuckets is off: the pipeline cannot reach it.</summary>
    NotReachable,

    /// <summary>Reachable, but the callers routed to the bucket are not allowed the pipeline.</summary>
    NotPermitted
}

/// <summary>
/// The one rule for which deposits may run the pipeline (issue #410), shared by RunPipelineHandler,
/// which enforces it, and /whoami, which reports it. The default working bucket always may - the
/// Pipeline API reads it through its mount. A caller's own bucket may only when the Pipeline API can
/// copy from it (AllowPipelineForCallerBuckets) AND the callers routed to it have allowPipeline:
/// reachability and permission are separate, and switching on the first must not decide the second.
/// </summary>
public static class PipelinePolicy
{
    public static PipelineAccess ForBucket(
        string? bucket, string? defaultBucket, bool allowPipelineForCallerBuckets, IClientDirectory clients)
    {
        if (!string.IsNullOrEmpty(bucket) && bucket == defaultBucket)
        {
            return PipelineAccess.Permitted;
        }
        if (!allowPipelineForCallerBuckets)
        {
            return PipelineAccess.NotReachable;
        }
        return clients.IsPipelineAllowedForDepositBucket(bucket) ? PipelineAccess.Permitted : PipelineAccess.NotPermitted;
    }
}
