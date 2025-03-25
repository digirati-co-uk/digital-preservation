using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Export;
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
    /// A low-cost way to determine the type of a resource, or whether a resource exists at the path.
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <returns></returns>
    Task<Result<string?>> GetResourceType(string path);
    /// <summary>
    /// 
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <param name="version">v1, v2 etc (optional - latest will be returned by default)</param>
    /// <returns></returns>
    Task<Result<ArchivalGroup?>> GetArchivalGroup(string path, string? version);
    
    Task<Result<Container?>> CreateContainer(string path, string? name = null);
    
    Task<Result<ImportJobResult>> ExecuteImportJob(ImportJob requestImportJob, CancellationToken cancellationToken = default);
    Task<Result<ImportJobResult>> GetImportJobResult(Uri storageApiImportJobResultUri);
    
    // Asynchronously export the entire contents of archivalGroup (version=versionToExport) to exportLocation
    Task<Result<Export>> ExportArchivalGroup(Uri archivalGroup, Uri exportLocation, string versionToExport, CancellationToken cancellationToken = default);
    
    // Synchronously export only the METS file from archivalGroup (version=versionToExport) to exportLocation
    Task<Result<Export>> ExportArchivalGroupMetsOnly(Uri archivalGroup, Uri exportLocation, string? versionToExport, CancellationToken cancellationToken = default);

    /// <summary>
    /// Basic ping to check Storage API is alive. 
    /// </summary>
    /// <remarks>This is intended for testing only, will be removed</remarks>
    /// <returns>true if alive, else false</returns>
    Task<ConnectivityCheckResult?> IsAlive(CancellationToken cancellationToken = default);
    Task<ConnectivityCheckResult?> CanSeeS3(CancellationToken cancellationToken = default);
    Task<Result> DeleteContainer(string path, bool requestPurge, CancellationToken cancellationToken);
    Task<Result<Export>> GetExport(Uri entityExportResultUri);
    
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <param name="version">v1, v2 etc (optional - latest will be returned by default)</param>
    /// <returns></returns>
    Task<Result<Stream>> GetBinaryStream(string path, string? version);

    Task<Result<ArchivalGroup?>> TestArchivalGroupPath(string archivalGroupPathUnderRoot);
    
    Task<Result<List<Activity>>> GetImportJobActivities(DateTime after, CancellationToken cancellationToken = default);
}