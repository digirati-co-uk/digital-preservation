using DigitalPreservation.Common.Model;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Options;

namespace Storage.API.Fedora.Model;

public class Converters
{
    private readonly FedoraOptions fedoraOptions;
    private readonly ConverterOptions converterOptions;
    
    private readonly string fedoraRoot;
    private readonly Uri fedoraRootUri;
    private readonly string repositoryRoot;
    private readonly string fedoraAgentRoot; // Not sure what this is going to be yet
    private readonly string agentRoot;
    private readonly Uri agentRootUri;
    private readonly string transientRoot;
    private readonly string importRoot;

    // We could register the vocab with Fedora and alias the type
    // But this will always work without having to do anything special to Fedora.
    // The only thing to watch out for here is if Fedora starts using this type in the future.
    // TODO: Consider minting our own URI for ArchivalGroupRdfType
    public const string ArchivalGroupRdfType = "http://purl.org/dc/dcmitype/Collection";
    
    public Converters(IOptions<FedoraOptions> fedoraOptions, IOptions<ConverterOptions> converterOptions)
    {
        this.fedoraOptions = fedoraOptions.Value;
        this.converterOptions = converterOptions.Value;
        fedoraRootUri = fedoraOptions.Value.Root;
        fedoraRoot = fedoraOptions.Value.Root.ToString();
        repositoryRoot = converterOptions.Value.RepositoryRoot.ToString();
        fedoraAgentRoot = fedoraOptions.Value.Root.ToString(); // make it same for now
        agentRoot = converterOptions.Value.AgentRoot.ToString();
        agentRootUri = new Uri(agentRoot);
        transientRoot = converterOptions.Value.TransientRoot.ToString();
        importRoot = converterOptions.Value.ImportRoot.ToString();
    }
    
    public ArchivalGroup MakeArchivalGroup(FedoraJsonLdResponse fedoraJsonLdResponse)
    {
        var archivalGroup = new ArchivalGroup();
        PopulateBaseFields(archivalGroup, fedoraJsonLdResponse);
        return archivalGroup;
    }
    
    public Binary MakeBinary(BinaryMetadataResponse binaryMetadataResponse)
    {
        var binary = new Binary();
        PopulateBaseFields(binary, binaryMetadataResponse);
        binary.ContentType = binaryMetadataResponse.ContentType;
        binary.Size = Convert.ToInt64(binaryMetadataResponse.Size);
        binary.Digest = binaryMetadataResponse.Digest?.Split(':')[^1];
        return binary;
    }
    
    public Container MakeContainer(FedoraJsonLdResponse fedoraJsonLdResponse)
    {
        var container = new Container();
        SetType(container, fedoraJsonLdResponse);
        PopulateBaseFields(container, fedoraJsonLdResponse);
        return container;
    }

    private static void SetType(Container container, FedoraJsonLdResponse fedoraJsonLdResponse)
    {
        if (fedoraJsonLdResponse.Type == null || fedoraJsonLdResponse.Type.Length == 0)
        {
            throw new InvalidOperationException("No type present");
        }
        if (fedoraJsonLdResponse.Type.Contains("fedora:RepositoryRoot"))
        {
            container.Type = "RepositoryRoot";
        }
        else if (fedoraJsonLdResponse.Type.Contains(ArchivalGroupRdfType))
        {
            container.Type = "ArchivalGroup";
        }
        else
        {
            container.Type = "Container";
        }
    }


    
    private void PopulateBaseFields(PreservedResource resource, FedoraJsonLdResponse fedoraJsonLdResponse)
    {
        resource.Id = ConvertToRepositoryUri(fedoraJsonLdResponse.Id);
        resource.Name = fedoraJsonLdResponse.Title;
        resource.Created = fedoraJsonLdResponse.Created;
        resource.CreatedBy = ConvertToAgentUri(fedoraJsonLdResponse.CreatedBy);
        resource.LastModified = fedoraJsonLdResponse.LastModified;
        resource.LastModifiedBy = ConvertToAgentUri(fedoraJsonLdResponse.LastModifiedBy);
    }

    public Uri ConvertToRepositoryUri(Uri fedoraUri)
    {
        var repositoryUri = fedoraUri.ToString().ReplaceFirst(fedoraRoot, repositoryRoot);
        return new Uri(repositoryUri);
    }

    public Uri RepositoryUriFromPathUnderRoot(string pathUnderRoot)
    {
        return new Uri(repositoryRoot + pathUnderRoot);
    }

    public Uri GetAgentUri(string identitySlug)
    {
        return new Uri(agentRoot + identitySlug);
    }

    private Uri? ConvertToAgentUri(string? fedoraAgentUri)
    {
        if (fedoraAgentUri.IsNullOrWhiteSpace()) return null;
        
        var agentUri = fedoraAgentUri.ReplaceFirst(fedoraAgentRoot, agentRoot);
        if (agentUri == fedoraAgentUri)
        {
            return new Uri(agentRootUri, agentUri);
        }
        return new Uri(agentUri);
    }

    internal Uri GetFedoraUri(string? pathUnderFedoraRoot)
    {
        return new Uri(fedoraOptions.Root, pathUnderFedoraRoot);
    }

    public bool IsFedoraRoot(Uri uri)
    {
        return uri == fedoraRootUri;
    }

    /// <summary>
    /// Use this for JSON responses that need an id, but are not recoverable from that id.
    /// Example - a diff import job generated by the storage API is transient
    /// </summary>
    /// <returns></returns>
    public Uri GetTransientResourceId(string? prefix = null)
    {
        return new Uri(transientRoot + (prefix == null ? "" : prefix + "/") + Guid.NewGuid());
    }

    public Uri GetStorageImportJobResultId(string archivalGroupPathUnderRoot, string jobIdentifier)
    {
        return new Uri($"{importRoot}results/{jobIdentifier}/{archivalGroupPathUnderRoot}");
    }
}