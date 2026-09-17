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
}
