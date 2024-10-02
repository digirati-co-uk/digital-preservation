using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using Storage.Repository.Common;

namespace Preservation.Client;

public interface IPreservationApiClient
{
    /// <summary>
    /// Retrieve a resource, which may be a container, binary, ArchivalGroup or the repository root.
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <returns></returns>
    Task<Result<PreservedResource?>> GetResource(string path);
    
    Task<Result<Container?>> CreateContainer(string path, string? name = null);
    Task<Result<Deposit?>> CreateDeposit(string? archivalGroupRepositoryPath, string? archivalGroupProposedName, string? submissionText, CancellationToken cancellationToken);
    Task<Result<List<Deposit>>> GetDeposits(DepositQuery? query, CancellationToken cancellationToken = default);
    
    
    
    
    
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