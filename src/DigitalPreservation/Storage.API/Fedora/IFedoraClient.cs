using System.Net;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
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
    Task<Result<PreservedResource?>> GetResource(string? pathUnderFedoraRoot, Transaction? transaction = null, CancellationToken cancellationToken = default);

    Task<Result<string?>> GetResourceType(string? pathUnderFedoraRoot, Transaction? transaction = null);
    // Task<Result<Container?>> ContainerCanBeCreatedAtPath(string pathUnderFedoraRoot, Transaction? transaction = null);
    Task<Result<Container?>> CreateContainer(string pathUnderFedoraRoot, string callerIdentity, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Result<Container?>> CreateContainerWithinArchivalGroup(string pathUnderFedoraRoot, string callerIdentity, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Result<ArchivalGroup?>> CreateArchivalGroup(string pathUnderFedoraRoot, string callerIdentity, string name, Transaction transaction, CancellationToken cancellationToken = default);

    Task<Result<Binary?>> PutBinary(Binary binary, string callerIdentity, Transaction transaction, CancellationToken cancellationToken = default);
    
    Task<Result<ArchivalGroup?>> GetPopulatedArchivalGroup(string pathUnderFedoraRoot, string? version = null, Transaction? transaction = null);

    Task<Result<ArchivalGroup?>> GetValidatedArchivalGroupForImportJob(string pathUnderFedoraRoot, Transaction? transaction = null);
    
    Task<Result<PreservedResource>> Delete(PreservedResource resource, string callerIdentity, Transaction transaction, CancellationToken cancellationToken = default);
    
    Task<Result> DeleteContainerOutsideOfArchivalGroup(string pathUnderFedoraRoot, string callerIdentity, bool purge, CancellationToken cancellationToken);
    
    Task<Result> UpdateContainerMetadata(string pathUnderFedoraRoot, string? name, string callerIdentity, Transaction transaction, CancellationToken cancellationToken = default);

    
    // Transactions
    Task<Transaction> BeginTransaction();
    Task<HttpStatusCode> GetTransactionHttpStatus(Transaction tx);
    Task KeepTransactionAlive(Transaction tx);
    Task CommitTransaction(Transaction tx, CancellationToken cancellationToken);
    Task RollbackTransaction(Transaction tx);
}