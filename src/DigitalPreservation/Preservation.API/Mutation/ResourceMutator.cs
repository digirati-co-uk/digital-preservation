using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
using Microsoft.Extensions.Options;

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
        resource.Id = MutateUri(resource.Id);
        resource.CreatedBy = MutateUri(resource.CreatedBy);
        resource.LastModifiedBy = MutateUri(resource.LastModifiedBy);
        resource.PartOf = MutateUri(resource.PartOf);
    }

    private Uri? MutateUri(Uri? uri)
    {
        // Discuss whether it's actually worth doing it this way:
        // https://stackoverflow.com/questions/479799/replace-host-in-uri
        
        if(uri == null) return null;
        
        return new Uri(preservationHost + uri.ToString().RemoveStart(storageHost));
    }
}

public class MutatorOptions
{
    public const string ResourceMutator = "ResourceMutator";
    
    public required string Storage { get; set; }
    public required string Preservation { get; set; }
}