namespace Storage.API.Fedora;

public interface IFedoraClient
{
    /// <summary>
    /// Basic ping to check Fedora API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<bool> IsAlive(CancellationToken cancellationToken = default);
}