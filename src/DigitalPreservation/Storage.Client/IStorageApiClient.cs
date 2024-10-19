using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using Storage.Repository.Common;

namespace Storage.Client;

public interface IStorageApiClient
{
    /// <summary>
    /// Retrieve a resource, which may be a container, binary, ArchivalGroup or the repository root.
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <returns></returns>
    Task<Result<PreservedResource?>> GetResource(string path);
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <param name="version">v1, v2 etc (optional - latest will be returned by default)</param>
    /// <returns></returns>
    Task<Result<ArchivalGroup?>> GetArchivalGroup(string path, string? version);
    
    Task<Result<Container?>> CreateContainer(string path, string? name = null);
    Task<Result<ImportJob>> GetImportJob(string archivalGroupPathUnderRoot, Uri sourceUri);

    /// <summary>
    /// Basic ping to check Storage API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default);
    Task<ConnectivityCheckResult?> CanSeeS3(CancellationToken cancellationToken = default);
}