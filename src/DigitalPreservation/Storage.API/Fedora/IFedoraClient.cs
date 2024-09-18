using Storage.Repository.Common;

namespace Storage.API.Fedora;

/// <summary>
/// Interface for interacting with Fedora
/// </summary>
/// <remarks>
/// We may want to split this eventually (ie read/write or for different operations) but single interface for now
/// </remarks>
public interface IFedoraClient
{
    /// <summary>
    /// Basic ping to check Fedora API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<ConnectivityCheckResult> IsAlive(CancellationToken cancellationToken = default);
}