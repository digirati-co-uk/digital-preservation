using System.Diagnostics.CodeAnalysis;

namespace DigitalPreservation.Core.Auth;

/// <summary>
/// In-memory <see cref="IClientDirectory"/> backed by the <c>KnownClients</c> configuration section.
/// App ids (GUIDs) are matched case-insensitively.
/// </summary>
public sealed class ClientDirectory : IClientDirectory
{
    private readonly Dictionary<string, ClientProfile> clients;

    public IReadOnlyCollection<string> DepositBuckets { get; }

    public ClientDirectory(IDictionary<string, ClientProfile> clients)
    {
        this.clients = new Dictionary<string, ClientProfile>(clients, StringComparer.OrdinalIgnoreCase);
        DepositBuckets = this.clients.Values
            .Select(p => p.DepositBucket)
            .Where(bucket => !string.IsNullOrEmpty(bucket))
            .Distinct(StringComparer.Ordinal)
            .ToList()!;
    }

    public bool TryResolve(string? appId, [NotNullWhen(true)] out ClientProfile? profile)
    {
        if (!string.IsNullOrEmpty(appId))
        {
            return clients.TryGetValue(appId, out profile);
        }

        profile = null;
        return false;
    }
}
