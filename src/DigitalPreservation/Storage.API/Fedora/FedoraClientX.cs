using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Storage.API.Fedora.Model;

namespace Storage.API.Fedora;

public static class FedoraClientX
{
    /// <summary>
    /// Scopes writes to Fedora to a single caller identity, so a handler that makes several calls
    /// as the same caller (e.g. <c>ExecuteImportJobHandler</c>, which creates containers, puts
    /// binaries and deletes resources all as the import job's <c>CreatedBy</c>) states that
    /// identity once instead of passing it to every call - issue #20.
    /// </summary>
    public static IIdentityScopedFedoraClient WithIdentity(this IFedoraClient fedoraClient, string callerIdentity) =>
        new IdentityScopedFedoraClient(fedoraClient, callerIdentity);

    private sealed class IdentityScopedFedoraClient(IFedoraClient inner, string callerIdentity) : IIdentityScopedFedoraClient
    {
        public Task<Result<Container?>> CreateContainer(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.CreateContainer(pathUnderFedoraRoot, callerIdentity, name, transaction, cancellationToken);

        public Task<Result<Container?>> CreateContainerWithinArchivalGroup(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.CreateContainerWithinArchivalGroup(pathUnderFedoraRoot, callerIdentity, name, transaction, cancellationToken);

        public Task<Result<ArchivalGroup?>> CreateArchivalGroup(string pathUnderFedoraRoot, string name, Transaction transaction, CancellationToken cancellationToken = default) =>
            inner.CreateArchivalGroup(pathUnderFedoraRoot, callerIdentity, name, transaction, cancellationToken);

        public Task<Result<Binary?>> PutBinary(Binary binary, Transaction transaction, CancellationToken cancellationToken = default) =>
            inner.PutBinary(binary, callerIdentity, transaction, cancellationToken);

        public Task<Result<PreservedResource>> Delete(PreservedResource resource, Transaction transaction, CancellationToken cancellationToken = default) =>
            inner.Delete(resource, callerIdentity, transaction, cancellationToken);

        public Task<Result> DeleteContainerOutsideOfArchivalGroup(string pathUnderFedoraRoot, bool purge, CancellationToken cancellationToken) =>
            inner.DeleteContainerOutsideOfArchivalGroup(pathUnderFedoraRoot, callerIdentity, purge, cancellationToken);

        public Task<Result> UpdateContainerMetadata(string pathUnderFedoraRoot, string? name, Transaction transaction, CancellationToken cancellationToken = default) =>
            inner.UpdateContainerMetadata(pathUnderFedoraRoot, name, callerIdentity, transaction, cancellationToken);
    }
}
