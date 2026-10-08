using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Storage.API.Fedora.Model;

namespace Storage.API.Fedora;

/// <summary>
/// The subset of <see cref="IFedoraClient"/> that writes to Fedora as a specific caller, with
/// <c>callerIdentity</c> already bound - obtained via <see cref="FedoraClientX.WithIdentity"/>.
/// Every other parameter, default and return type matches the corresponding <see cref="IFedoraClient"/>
/// method exactly; this is a narrowing, not a different contract.
/// </summary>
public interface IIdentityScopedFedoraClient
{
    Task<Result<Container?>> CreateContainer(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Result<Container?>> CreateContainerWithinArchivalGroup(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default);
    Task<Result<ArchivalGroup?>> CreateArchivalGroup(string pathUnderFedoraRoot, string name, Transaction transaction, CancellationToken cancellationToken = default);
    Task<Result<Binary?>> PutBinary(Binary binary, Transaction transaction, CancellationToken cancellationToken = default);
    Task<Result<PreservedResource>> Delete(PreservedResource resource, Transaction transaction, CancellationToken cancellationToken = default);
    Task<Result> DeleteContainerOutsideOfArchivalGroup(string pathUnderFedoraRoot, bool purge, CancellationToken cancellationToken);
    Task<Result> UpdateContainerMetadata(string pathUnderFedoraRoot, string? name, Transaction transaction, CancellationToken cancellationToken = default);
}
