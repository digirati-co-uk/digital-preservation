using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Mutation;

/// <summary>
/// Changes PreservedResource URIs from Storage API to Preservation API
/// </summary>
/// <param name="options"></param>
public class ResourceMutator(IOptions<MutatorOptions> options)
{
    private readonly string storageHost = options.Value.Storage;
    private readonly string preservationHost = options.Value.Preservation;
    
    internal PreservedResource? MutateStorageResource(PreservedResource? storageResource)
    {
        if (storageResource == null) return null;
        
        MutateBaseUris(storageResource);

        if (storageResource is Container container)
        {
            foreach (var childContainer in container.Containers)
            {
                MutateStorageResource(childContainer);    
            }
            foreach (var childBinary in container.Binaries)
            {
                MutateStorageResource(childBinary);    
            }
        }
        
        return storageResource;
    }


    private void MutateBaseUris(PreservedResource resource)
    {
        resource.Id = MutateStorageApiUri(resource.Id);
        resource.CreatedBy = MutateStorageApiUri(resource.CreatedBy);
        resource.LastModifiedBy = MutateStorageApiUri(resource.LastModifiedBy);
        resource.PartOf = MutateStorageApiUri(resource.PartOf);
    }

    private Uri? MutateStorageApiUri(Uri? uri)
    {
        // Discuss whether it's actually worth doing it this way:
        // https://stackoverflow.com/questions/479799/replace-host-in-uri
        
        if(uri == null) return null;
        
        return new Uri(preservationHost + uri.ToString().RemoveStart(storageHost));
    }

    public Deposit MutateDeposit(DepositEntity entity)
    {            
        var deposit = new Deposit
        {
            Id = new Uri($"{preservationHost}/{Deposit.BasePathElement}/{entity.MintedId}"),
            ArchivalGroup = entity.ArchivalGroupPathUnderRoot.HasText()
                ? new Uri($"{preservationHost}/{PreservedResource.BasePathElement}/{entity.ArchivalGroupPathUnderRoot}")
                : null,
            ArchivalGroupName = entity.ArchivalGroupProposedName,
            Files = entity.Files,
            Active = entity.Active,
            Status = entity.Status,
            SubmissionText = entity.SubmissionText,
            Created = entity.Created,
            CreatedBy = new Uri($"{preservationHost}/{Agent.BasePathElement}/{entity.CreatedBy}"),
            LastModified = entity.LastModified,
            LastModifiedBy = new Uri($"{preservationHost}/{Agent.BasePathElement}/{entity.LastModifiedBy}"),
            Preserved = entity.Preserved,
            PreservedBy = entity.PreservedBy.HasText()
                ? new Uri($"{preservationHost}/{Agent.BasePathElement}/{entity.PreservedBy}")
                : null,
            Exported = entity.Exported,
            ExportedBy = entity.ExportedBy.HasText()
                ? new Uri($"{preservationHost}/{Agent.BasePathElement}/{entity.ExportedBy}")
                : null,
            VersionExported = entity.VersionExported,
            VersionPreserved = entity.VersionPreserved
        };
        return deposit;
    }

    public List<Deposit> MutateDeposits(IEnumerable<DepositEntity> deposits)
    {
        return deposits.Select(MutateDeposit).ToList();
    }
}

public class MutatorOptions
{
    public const string ResourceMutator = "ResourceMutator";
    
    public required string Storage { get; set; }
    public required string Preservation { get; set; }
}