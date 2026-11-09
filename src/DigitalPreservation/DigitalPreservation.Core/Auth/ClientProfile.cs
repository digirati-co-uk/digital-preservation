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
    /// and from then on the deposit's own Files URI is authoritative. Whether deposits in this
    /// bucket may run the pipeline is <see cref="AllowPipeline"/>, not the bucket itself (issue #410).
    /// </summary>
    public string? DepositBucket { get; init; }

    /// <summary>
    /// Whether deposits in this caller's <see cref="DepositBucket"/> may run the pipeline - whoever
    /// asks, including a person later working on one in the UI (issue #410). Off by default: a
    /// Goobi-class caller brings its own characterisation and virus scanning, a script-class caller
    /// (eTheses) needs the platform's. Deposits in the default working bucket are unaffected.
    /// </summary>
    public bool AllowPipeline { get; init; }
}
