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
    /// Optional deposit bucket this caller's deposits are routed to (RFC-0001 §8): deposit
    /// creation carves the working files area here instead of <c>AwsStorage:DefaultWorkingBucket</c>,
    /// and from then on the deposit's own Files URI is authoritative. Deposits outside the default
    /// bucket cannot run the pipeline, which mounts only the default bucket as a filesystem.
    /// </summary>
    public string? DepositBucket { get; init; }
}
