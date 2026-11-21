namespace DigitalPreservation.Core.Auth;

/// <summary>
/// The calling identity as the API resolved it, returned by the <c>/whoami</c> endpoint (RFC-0001 §9.1).
/// Self-only: it describes the caller making the request, never other callers or the KnownClients map.
/// </summary>
public sealed class WhoAmIResult
{
    /// <summary>Friendly name the caller's actions are attributed to.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// How <see cref="Name"/> was derived — see the <c>Source*</c> constants on
    /// <see cref="CallerResolver"/>: <c>user</c>, <c>token</c>, <c>header-fallback</c> or <c>unknown</c>.
    /// During the RFC-0001 migration a caller watches this flip from <c>header-fallback</c> to
    /// <c>token</c> when it is repointed.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>The signed <c>azp</c>/<c>appid</c> claim, when present.</summary>
    public string? AppId { get; init; }

    /// <summary>
    /// Bucket where this caller's deposits would be created — the caller's profile bucket if one is
    /// configured, otherwise the API's default working bucket. Callers may interact with it directly.
    /// </summary>
    public required string DepositBucket { get; init; }

    /// <summary>
    /// Whether deposits in <see cref="DepositBucket"/> may run the pipeline (issue #410). Set by the
    /// Preservation API, which owns that policy; null where it was not determined.
    /// </summary>
    public bool? PipelineAllowed { get; set; }

    /// <summary>
    /// Whether the token carries the <c>Preservation.Call</c> app role or the <c>access_as_user</c>
    /// delegated scope - what <c>FeatureFlags:RequireCallerRole</c> would demand (issue #293). Check
    /// it reads <c>true</c> for every caller before turning that flag on.
    /// </summary>
    public bool HasCallerPermission { get; init; }
}
