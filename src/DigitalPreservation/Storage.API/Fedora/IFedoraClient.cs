using DigitalPreservation.Common.Model;
using Storage.API.Fedora.Model;
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

    /// <summary>
    /// Get a resource from fedora
    /// </summary>
    /// <param name="pathUnderFedoraRoot">The path within the repository - i.e., not including /fcrepo/rest/ or /repository/</param>
    /// <param name="transaction"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<PreservedResource?> GetResource(string? pathUnderFedoraRoot, Transaction? transaction = null, CancellationToken cancellationToken = default);

    Task<Container?> CreateContainer(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default);
}