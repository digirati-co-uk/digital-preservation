using DigitalPreservation.Common.Model;
using Storage.Repository.Common;

namespace Preservation.Client;

public interface IPreservationApiClient
{
    Task<PreservedResource?> GetResource(string path);
    
    Task<Container?> CreateContainer(string path, string? name = null);
    
    
    
    // Healthchecks and housekeeping
    
    /// <summary>
    /// Basic ping to check Preservation API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default);
    Task<ConnectivityCheckResult?> CanTalkToS3(CancellationToken cancellationToken);
    Task<ConnectivityCheckResult?> CanSeeThatStorageCanTalkToS3(CancellationToken cancellationToken);
}