using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Utils;
using LeedsDlipServices.Identity;
using Microsoft.Extensions.Options;
using Preservation.API.Data.Entities;
using Deposit = DigitalPreservation.Common.Model.PreservationApi.Deposit;
using DepositEntity = Preservation.API.Data.Entities.Deposit;
using ImportJob = DigitalPreservation.Common.Model.Import.ImportJob;

namespace Preservation.API.Mutation;

/// <summary>
/// Changes PreservedResource URIs from Storage API to Preservation API
/// </summary>
/// <param name="options"></param>
public class ResourceMutator(
    IOptions<MutatorOptions> options)
{
    private readonly string storageHost = options.Value.Storage;
    private readonly string preservationHost = options.Value.Preservation;

    public Uri StorageUri { get; } = new(options.Value.Storage);
    public Uri PreservationUri { get; } = new(options.Value.Preservation);

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
        if (uri.ToString().StartsWith(storageHost))
        {
            var builder = new UriBuilder(uri)
            {
                Host = PreservationUri.Host,
                Port = PreservationUri.Port,
                Scheme = PreservationUri.Scheme
            };
            return new Uri(builder.Uri.ToString()); // ensures that OriginalString is preserved
        }
        return uri;
        // var uriS = uri.GetStringTemporaryForTesting();
        // if (uriS.StartsWith(storageHost))
        // {
        //     return new Uri(preservationHost + uriS.RemoveStart(storageHost));
        // }

    }
    
    
    public Uri? MutatePreservationApiUri(Uri? uri)
    {
        // Discuss whether it's actually worth doing it this way:
        // https://stackoverflow.com/questions/479799/replace-host-in-uri
        
        if(uri == null) return null;
        if (uri.ToString().StartsWith(preservationHost))
        {
            var builder = new UriBuilder(uri)
            {
                Host = StorageUri.Host,
                Port = StorageUri.Port,
                Scheme = StorageUri.Scheme
            };
            return new Uri(builder.Uri.ToString()); // ensures that OriginalString is preserved
        }
        return uri;
        // var uriS = uri.GetStringTemporaryForTesting();
        // if (uriS.StartsWith(preservationHost))
        // {
        //     return new Uri(storageHost + uriS.RemoveStart(preservationHost));
        // }
        //
        // return uri;
    }

    public Uri? GetAgentUri(string? agentName)
    {
        if (agentName.HasText())
        {
            return new Uri($"{preservationHost}/{Agent.BasePathElement}/{agentName}");
        }
        return null;
    }

    public string? GetCallerIdentity(Uri? agentUri)
    {
        return agentUri?.GetSlug();
    }
    
    public Uri GetActivityStreamUri(string path)
    {
        return new Uri($"{preservationHost}/activity/{path}");
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
            CreatedBy = GetAgentUri(entity.CreatedBy),
            LastModified = entity.LastModified,
            LastModifiedBy = GetAgentUri(entity.LastModifiedBy),
            Preserved = entity.Preserved,
            PreservedBy = GetAgentUri(entity.PreservedBy),
            Exported = entity.Exported,
            ExportedBy = GetAgentUri(entity.ExportedBy),
            // ExportResult = entity.ExportResultUri, // don't do this, just rely on the Status value.
            VersionExported = entity.VersionExported,
            VersionPreserved = entity.VersionPreserved,
            LockedBy = GetAgentUri(entity.LockedBy),
            LockDate = entity.LockDate 
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
    
        
    public ProcessPipelineResult MutatePipelineRunJob(PipelineRunJob entity)
    {            
        var errors = new List<Error>();
        if (!string.IsNullOrEmpty(entity.Errors))
        {
            errors.Add(new Error
            {
                Message = entity.Errors
            });
        }
        var processPipelineResult = new ProcessPipelineResult
        {
            Id = GetProcessPipelineResultUri(entity.Deposit, entity.Id),
            JobId = entity.Id,
            ArchivalGroupName = entity.ArchivalGroup,
            Status = entity.Status,
            Created = entity.DateSubmitted,
            DateBegun = entity.DateBegun,
            DateFinished = entity.DateFinished,
            CreatedBy = GetAgentUri(entity.RunUser),
            LastModified = entity.LastUpdated,
            LastModifiedBy = GetAgentUri(entity.RunUser),
            RunUser = entity.RunUser,
            Errors = errors.Count != 0 ? errors.ToArray<Error>() : null,
            Deposit = entity.Deposit,
            VirusDefinition = entity.VirusDefinition
        };
        return processPipelineResult;
    }
    
    private Uri GetProcessPipelineResultUri(string depositId, string jobId)
    {
        return new Uri($"{preservationHost}/{Deposit.BasePathElement}/{depositId}/pipelinerunjobs/{jobId}");
    }
    
    public List<ProcessPipelineResult> MutatePipelineRunJobs(IEnumerable<PipelineRunJob> pipelineRunJobs)
    {
        return pipelineRunJobs.Select(MutatePipelineRunJob).ToList();
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

    public Identifier MutateIdentityRecord(IdentityRecord record) =>
      new()
        {
            Id = record.Id,
            EPid = record.EPid,
            Created = record.Created is null ? null : Convert.ToDateTime(record.Created),
            Updated = record.Updated is null ? null : Convert.ToDateTime(record.Updated),
            CatIrn = record.CatIrn,
            Desc = record.Desc,
            Status = record.Status,
            Title = record.Title,
            CatalogueApiUri = record.CatalogueApiUri,
            ManifestUri = record.ManifestUri,
            RepositoryUri = record.RepositoryUri
        };
    
}

public class MutatorOptions
{
    public const string ResourceMutator = "ResourceMutator";
    
    public required string Storage { get; set; }
    public required string Preservation { get; set; }
}