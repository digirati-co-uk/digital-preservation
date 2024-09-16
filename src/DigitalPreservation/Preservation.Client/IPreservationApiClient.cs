namespace Preservation.Client;

public interface IPreservationApiClient
{
    /// <summary>
    /// Basic ping to check Preservation API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<bool> IsAlive(CancellationToken cancellationToken = default);

    Task<bool> CanTalkToS3(CancellationToken cancellationToken);
    Task<bool> CanSeeThatStorageCanTalkToS3(CancellationToken cancellationToken);
}