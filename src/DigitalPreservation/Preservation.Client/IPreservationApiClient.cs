using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Import;
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
    
    /// <summary>
    /// A low-cost way to determine the type of a resource, or whether a resource exists at the path.
    /// </summary>
    /// <param name="path">The full path including the /repository/ initial path element</param>
    /// <returns></returns>
    Task<Result<string?>> GetResourceType(string path);
    
    Task<Result<Container?>> CreateContainer(string path, string? name = null);
    Task<Result<Deposit?>> CreateDeposit(
        string? archivalGroupRepositoryPath, 
        string? archivalGroupProposedName, 
        string? submissionText, 
        bool useObjectTemplate, 
        bool export,
        string? exportVersion,
        CancellationToken cancellationToken);
    
    Task<Result<Deposit?>> CreateDepositFromIdentifier(string schema, string identifier, CancellationToken cancellationToken);
    Task<Result<DepositQueryPage>> GetDeposits(DepositQuery? query, CancellationToken cancellationToken = default);
    Task<Result<Deposit?>> GetDeposit(string id, CancellationToken cancellationToken = default);
    Task<Result<Deposit?>> UpdateDeposit(Deposit deposit, CancellationToken cancellationToken);
    Task<Result> DeleteDeposit(string id, CancellationToken cancellationToken);
    Task<Result<List<ImportJobResult>>> GetImportJobResultsForDeposit(string depositId, CancellationToken cancellationToken);
    Task<Result<ImportJobResult>> GetImportJobResult(string depositId, string importJobResultId, CancellationToken cancellationToken);
    Task<Result<ImportJob>> GetDiffImportJob(string depositId, CancellationToken cancellationToken);
    Task<Result<ImportJobResult>> SendDiffImportJob(string depositId, CancellationToken cancellationToken);
    Task<Result> DeleteContainer(string path, bool requestPurge, CancellationToken cancellationToken);
    Task<Result<List<Uri>>> GetAllAgents(CancellationToken cancellationToken);
    
    Task<Result<(string, string)>> GetMetsWithETag(string depositId, CancellationToken cancellationToken);
    
    Task<Result<ArchivalGroup?>> TestArchivalGroupPath(string archivalGroupPathUnderRoot);
    Task<(Stream?, string?)> GetContentStream(string repositoryPath, CancellationToken cancellationToken);
    Task<(Stream?, string?)> GetMetsStream(string archivalGroupPathUnderRoot, CancellationToken cancellationToken = default);
    
    Task<OrderedCollection?> GetOrderedCollection(string stream);
    Task<OrderedCollectionPage?> GetOrderedCollectionPage(string stream, int index);
    
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