using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;
using DepositEntity = Preservation.API.Data.Entities.Deposit; 

namespace Preservation.API.Mutation;

/// <summary>
/// Changes PreservedResource URIs from Storage API to Preservation API
/// </summary>
/// <param name="options"></param>
public class ResourceMutator(
    ILogger<ResourceMutator> logger,
    IOptions<MutatorOptions> options)
{
    private readonly string storageHost = options.Value.Storage;
    private readonly string preservationHost = options.Value.Preservation;
    
    internal PreservedResource? MutateStorageResource(PreservedResource? storageResource)
    {
        if (storageResource == null) return null;
        
        MutateStorageBaseUris(storageResource);

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
    
    internal PreservedResource? MutatePreservationResource(PreservedResource? preservationResource)
    {
        if (preservationResource == null) return null;
        
        MutatePreservationBaseUris(preservationResource);

        if (preservationResource is Container container)
        {
            foreach (var childContainer in container.Containers)
            {
                MutatePreservationResource(childContainer);    
            }
            foreach (var childBinary in container.Binaries)
            {
                MutatePreservationResource(childBinary);    
            }
        }
        
        return preservationResource;
    }

    private void MutateStorageBaseUris(PreservedResource resource)
    {
        MutateStorageBaseUris((Resource)resource);
        resource.PartOf = MutateStorageApiUri(resource.PartOf);
    }

    private void MutateStorageBaseUris(Resource resource)
    {
        resource.Id = MutateStorageApiUri(resource.Id);
        resource.CreatedBy = MutateStorageApiUri(resource.CreatedBy);
        resource.LastModifiedBy = MutateStorageApiUri(resource.LastModifiedBy);
    }
    
    
    
    private void MutatePreservationBaseUris(PreservedResource resource)
    {
        MutatePreservationBaseUris((Resource)resource);
        resource.PartOf = MutatePreservationApiUri(resource.PartOf);
    }

    private void MutatePreservationBaseUris(Resource resource)
    {
        resource.Id = MutatePreservationApiUri(resource.Id);
        resource.CreatedBy = MutatePreservationApiUri(resource.CreatedBy);
        resource.LastModifiedBy = MutatePreservationApiUri(resource.LastModifiedBy);
    }
    

    private Uri? MutateStorageApiUri(Uri? uri)
    {
        // Discuss whether it's actually worth doing it this way:
        // https://stackoverflow.com/questions/479799/replace-host-in-uri
        
        if(uri == null) return null;
        var uriS = uri.ToString();
        if (uriS.StartsWith(storageHost))
        {
            return new Uri(preservationHost + uriS.RemoveStart(storageHost));
        }

        return uri;
    }
    
    
    private Uri? MutatePreservationApiUri(Uri? uri)
    {
        // Discuss whether it's actually worth doing it this way:
        // https://stackoverflow.com/questions/479799/replace-host-in-uri
        
        if(uri == null) return null;
        var uriS = uri.ToString();
        if (uriS.StartsWith(preservationHost))
        {
            return new Uri(storageHost + uriS.RemoveStart(preservationHost));
        }

        return uri;
    }

    public Deposit MutateDeposit(DepositEntity entity)
    {            
        var deposit = new Deposit
        {
            Id = GetDepositUri(entity.MintedId),
            ArchivalGroup = entity.ArchivalGroupPathUnderRoot.HasText()
                ? new Uri($"{preservationHost}/{PreservedResource.BasePathElement}/{entity.ArchivalGroupPathUnderRoot}")
                : null,
            ArchivalGroupName = entity.ArchivalGroupName,
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

    private Uri GetDepositUri(string depositId)
    {
        return new Uri($"{preservationHost}/{Deposit.BasePathElement}/{depositId}");
    }

    public List<Deposit> MutateDeposits(IEnumerable<DepositEntity> deposits)
    {
        return deposits.Select(MutateDeposit).ToList();
    }

    public void MutateStorageImportJob(ImportJob storageImportJob)
    {
        MutateStorageBaseUris(storageImportJob);
        storageImportJob.ArchivalGroup = MutateStorageApiUri(storageImportJob.ArchivalGroup)!;
        foreach (var container in storageImportJob.ContainersToAdd)
        {
            MutateStorageResource(container);
        }
        foreach (var container in storageImportJob.ContainersToDelete)
        {
            MutateStorageResource(container);
        }
        foreach (var binary in storageImportJob.BinariesToAdd)
        {
            MutateStorageResource(binary);
        }
        foreach (var binary in storageImportJob.BinariesToDelete)
        {
            MutateStorageResource(binary);
        }
        foreach (var binary in storageImportJob.BinariesToPatch)
        {
            MutateStorageResource(binary);
        }
        foreach (var container in storageImportJob.ContainersToRename)
        {
            MutateStorageResource(container);
        }
        foreach (var binary in storageImportJob.BinariesToRename)
        {
            MutateStorageResource(binary);
        }
    }

    public void MutatePreservationImportJob(ImportJob preservationImportJob)
    {
        MutatePreservationBaseUris(preservationImportJob);
        preservationImportJob.ArchivalGroup = MutatePreservationApiUri(preservationImportJob.ArchivalGroup)!;
        foreach (var container in preservationImportJob.ContainersToAdd)
        {
            MutatePreservationResource(container);
        }
        foreach (var container in preservationImportJob.ContainersToDelete)
        {
            MutatePreservationResource(container);
        }
        foreach (var binary in preservationImportJob.BinariesToAdd)
        {
            MutatePreservationResource(binary);
        }
        foreach (var binary in preservationImportJob.BinariesToDelete)
        {
            MutatePreservationResource(binary);
        }
        foreach (var binary in preservationImportJob.BinariesToPatch)
        {
            MutatePreservationResource(binary);
        }
        foreach (var container in preservationImportJob.ContainersToRename)
        {
            MutatePreservationResource(container);
        }
        foreach (var binary in preservationImportJob.BinariesToRename)
        {
            MutatePreservationResource(binary);
        }
    }

    public void MutateStorageImportJobResult(ImportJobResult storageImportJobResult, string depositId, string resultId)
    {
        MutateStorageImportJobResult(storageImportJobResult, GetDepositUri(depositId), resultId);
    }

    public void MutateStorageImportJobResult(ImportJobResult storageImportJobResult, Uri deposit, string resultId)
    {
        MutateStorageBaseUris(storageImportJobResult);
        storageImportJobResult.Id = new Uri($"{deposit}/importjobs/results/{resultId}");
        storageImportJobResult.Deposit = deposit;
        storageImportJobResult.ArchivalGroup = MutateStorageApiUri(storageImportJobResult.ArchivalGroup)!;
        foreach (var container in storageImportJobResult.ContainersAdded)
        {
            MutateStorageResource(container);
        }
        foreach (var container in storageImportJobResult.ContainersDeleted)
        {
            MutateStorageResource(container);
        }
        foreach (var binary in storageImportJobResult.BinariesAdded)
        {
            MutateStorageResource(binary);
        }
        foreach (var binary in storageImportJobResult.BinariesDeleted)
        {
            MutateStorageResource(binary);
        }
        foreach (var binary in storageImportJobResult.BinariesPatched)
        {
            MutateStorageResource(binary);
        }
        foreach (var container in storageImportJobResult.ContainersRenamed)
        {
            MutateStorageResource(container);
        }
        foreach (var binary in storageImportJobResult.BinariesRenamed)
        {
            MutateStorageResource(binary);
        }
    }
}

public class MutatorOptions
{
    public const string ResourceMutator = "ResourceMutator";
    
    public required string Storage { get; set; }
    public required string Preservation { get; set; }
}