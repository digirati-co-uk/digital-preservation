using System.Diagnostics.CodeAnalysis;

namespace DigitalPreservation.Core.Auth;

/// <summary>
/// Closed allow-list of known machine callers, keyed by the signed <c>azp</c>/<c>appid</c> claim.
/// Doubles as friendly-name resolution and (eventually) per-caller policy. See RFC-0001 §5.2.
/// </summary>
public interface IClientDirectory
{
    /// <summary>
    /// Resolves a caller's app id to its <see cref="ClientProfile"/>. Returns <c>false</c> for a
    /// null/empty or unrecognised app id — an unknown caller is never resolved.
    /// </summary>
    bool TryResolve(string? appId, [NotNullWhen(true)] out ClientProfile? profile);

    /// <summary>
    /// Every distinct, non-empty <see cref="ClientProfile.DepositBucket"/> across every known
    /// client, compared ordinally (S3 bucket names are lower-case). Together with the API's default
    /// working bucket, this is the platform's whole set of deposit buckets - used to validate an
    /// export destination without a separate allow-list (issue #288).
    /// </summary>
    IReadOnlyCollection<string> DepositBuckets { get; }

    /// <summary>
    /// Whether deposits in <paramref name="bucket"/>, a caller's <see cref="ClientProfile.DepositBucket"/>,
    /// may run the pipeline (issue #410): true only when at least one known client routes to it and
    /// every client that does has <see cref="ClientProfile.AllowPipeline"/>, so a bucket shared with a
    /// caller that should stay excluded is never opened by another. False for a bucket no client
    /// routes to - including the default working bucket, which the caller checks separately.
    /// </summary>
    bool IsPipelineAllowedForDepositBucket(string? bucket);
}
