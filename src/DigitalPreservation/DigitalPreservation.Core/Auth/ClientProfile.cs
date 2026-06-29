namespace DigitalPreservation.Core.Auth;

/// <summary>
/// Per-caller profile resolved from a known machine caller's app id (<c>azp</c>/<c>appid</c>).
/// Populated from the <c>KnownClients</c> configuration section (see <see cref="IClientDirectory"/>).
/// </summary>
public sealed class ClientProfile
{
    /// <summary>Friendly name the caller's actions are attributed to (logs, METS authorship).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional deposit bucket this caller's deposits should be routed to. Modeled here for the
    /// RFC-0001 Goobi use case but <b>not consumed in Phase 0</b> — deposit creation still uses
    /// <c>AwsStorage:DefaultWorkingBucket</c>. Bucket routing is a later phase (RFC §8).
    /// </summary>
    public string? DepositBucket { get; init; }
}
