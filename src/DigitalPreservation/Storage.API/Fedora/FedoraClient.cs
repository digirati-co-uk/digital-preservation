using System.Text.Json;
using DigitalPreservation.Common.Model;
using Storage.API.Fedora.Http;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;

namespace Storage.API.Fedora;

internal class FedoraClient(
    HttpClient httpClient,
    ILogger<FedoraClient> logger,
    Converters converters) : IFedoraClient
{
    public async Task<PreservedResource?> GetResource(string? pathUnderFedoraRoot, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var uri = converters.GetFedoraUri(pathUnderFedoraRoot);
        PreservedResource? resource;
        
        // See proto FedoraWrapper:514 and 779
        // First pass, assume it is a container. And don't find parent AG.
        // Later we'll need to interrogate
        
        // 514:
        // getResourceInfo
        
        // is it an AG? => MakeAG
        
        // walk up to tree to find AG (if exists) => Get (cached) storageMap
        // first pass - it won't be in an AG because we can't make AGs yet
        
        // Is it a binary?
        
        // Is it a container? => 779 GetPopulatedContainer(..)
        
        // proto version takes objectversion but does nothing with it - see comments there
        resource = await GetPopulatedContainer(uri, false, false, transaction);
        
        // set partOf
        
        return resource;
    }

    private async Task<Container?> GetPopulatedContainer(Uri uri, bool isArchivalGroup, bool recurse, Transaction? transaction = null)
    {
        var request = MakeHttpRequestMessage(uri, HttpMethod.Get)
            .ForJsonLd()
            .WithContainedDescriptions();
        
        var response = await httpClient.SendAsync(request);
        
        // check for archival group as per lines 847-855
        
        
        // WithContainedDescriptions could return @graph or it could return a single object if the container has no children
        // This is the only place that needs to check for @graph so can remain here
        var stream = await response.Content.ReadAsStreamAsync();
        
        using var jDoc = await JsonDocument.ParseAsync(stream); // jDoc is a Fedora JSON response
        
        JsonElement[]? containerAndContained = null;
        if (jDoc.RootElement.TryGetProperty("@graph", out JsonElement graph))
        {
            containerAndContained = [.. graph.EnumerateArray()];
        }
        else
        {
            if (jDoc.RootElement.TryGetProperty("@id", out JsonElement idElement))
            {
                containerAndContained = [jDoc.RootElement];
            }
        }
        if (containerAndContained == null || containerAndContained.Length == 0) 
        {
            throw new InvalidOperationException("Could not parse Container response");
        }
        if (containerAndContained[0].GetProperty("@id").GetString() != uri.ToString())
        {
            throw new InvalidOperationException("First resource in @graph should be the asked-for URI");
        }
            
        // Make a map of the IDs
        Dictionary<string, JsonElement> dict = containerAndContained.ToDictionary(x => x.GetProperty("@id").GetString()!);
        var fedoraObject = containerAndContained[0].Deserialize<FedoraJsonLdResponse>();
        var topContainer = isArchivalGroup ? converters.MakeArchivalGroup(fedoraObject!) : converters.MakeContainer(fedoraObject!);

        // Get the contains property which may be a single value or an array
        foreach (var id in GetIdsFromContainsProperty(containerAndContained[0]))
        {
            var resource = dict[id];
            if (resource.HasType("fedora:Container"))
            {
                var fedoraContainer = resource.Deserialize<FedoraJsonLdResponse>()!;
                Container? container;
                if (recurse)
                {
                    container = await GetPopulatedContainer(fedoraContainer.Id, false, true, transaction);
                }
                else
                {
                    container = converters.MakeContainer(fedoraContainer);
                }
                topContainer.Containers.Add(container!);
            }
            else if (resource.HasType("fedora:Binary"))
            {
                var fedoraBinary = resource.Deserialize<BinaryMetadataResponse>();
                var binary = converters.MakeBinary(fedoraBinary!);
                topContainer.Binaries.Add(binary);
            }
        }
        return topContainer;
    }


    private static HttpRequestMessage MakeHttpRequestMessage(Uri uri, HttpMethod method)
    {
        var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Headers.Accept.Clear();
        return requestMessage;
    }
    
    private List<string> GetIdsFromContainsProperty(JsonElement element)
    {
        List<string> childIds = [];
        if (element.TryGetProperty("contains", out JsonElement contains))
        {
            if (contains.ValueKind == JsonValueKind.String)
            {
                childIds = [contains.GetString()!];
            }
            else if (contains.ValueKind == JsonValueKind.Array)
            {
                childIds = contains.EnumerateArray().Select(x => x.GetString()!).ToList();
            }
        }
        return childIds;
    }


    public async Task<ConnectivityCheckResult> IsAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync("./fcr:systeminfo", cancellationToken);
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd,
                Success = true
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occured while checking if Fedora is alive");
            return new ConnectivityCheckResult
            {
                Name = ConnectivityCheckResult.DigitalPreservationBackEnd,
                Success = false,
                Error = ex.Message
            };
        }
    }
}