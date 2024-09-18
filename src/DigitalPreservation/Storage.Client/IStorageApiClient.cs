using Storage.Repository.Common;

namespace Storage.Client;

public interface IStorageApiClient
{
    /// <summary>
    /// Basic ping to check Storage API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default);
    Task<ConnectivityCheckResult?> CanSeeS3(CancellationToken cancellationToken = default);
}