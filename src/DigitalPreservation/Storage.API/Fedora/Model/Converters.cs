using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
using Microsoft.Extensions.Options;

namespace Storage.API.Fedora.Model;

public class Converters
{
    private readonly FedoraOptions fedoraOptions;
    private readonly ConverterOptions converterOptions;
    
    private readonly string fedoraRoot;
    private readonly string repositoryRoot;
    private readonly string fedoraAgentRoot; // Not sure what this is going to be yet
    private readonly string agentRoot;
    private readonly Uri agentRootUri;
    
    public Converters(IOptions<FedoraOptions> fedoraOptions, IOptions<ConverterOptions> converterOptions)
    {
        this.fedoraOptions = fedoraOptions.Value;
        this.converterOptions = converterOptions.Value;
        fedoraRoot = fedoraOptions.Value.Root.ToString();
        repositoryRoot = converterOptions.Value.RepositoryRoot.ToString();
        fedoraAgentRoot = fedoraOptions.Value.Root.ToString(); // make it same for now
        agentRoot = converterOptions.Value.AgentRoot.ToString();
        agentRootUri = new Uri(agentRoot);
    }
    
    public ArchivalGroup MakeArchivalGroup(FedoraJsonLdResponse fedoraJsonLdResponse)
    {
        throw new NotImplementedException();
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
        else if (fedoraJsonLdResponse.Type.Contains("http://purl.org/dc/dcmitype/Collection"))
        {
            // TODO - introduce this dcmi namespace and also check for dcmi:Collection (or whatever prefix)
            container.Type = "ArchivalGroup";
        }
        else
        {
            container.Type = "Container";
        }
    }


    public Binary MakeBinary(BinaryMetadataResponse binaryMetadataResponse)
    {
        throw new NotImplementedException();
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

    private Uri ConvertToRepositoryUri(Uri fedoraUri)
    {
        var repositoryUri = fedoraUri.ToString().ReplaceFirst(fedoraRoot, repositoryRoot);
        return new Uri(repositoryUri);
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
    
}