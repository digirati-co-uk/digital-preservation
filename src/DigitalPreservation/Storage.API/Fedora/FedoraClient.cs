using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Utils;
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

    public async Task<Container?> CreateContainer(string pathUnderFedoraRoot, string? name, Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        // TODO: Validate New Container
        // Does the slug only contain valid chars? ✓
        // Is there already something at this path? - no ✓
        // Is there a Container at the parent of this path - yes ✓
        // Is the parent Container an Archival Group or part of an Archival Group? no ✓
        // OK...
        return await CreateContainerInternal(false, pathUnderFedoraRoot, name, transaction);
    }

    private async Task<Container?> CreateContainerInternal(
        bool asArchivalGroup,
        string pathUnderFedoraRoot,
        string? name,
        Transaction? transaction = null)
    {
        // TODO: This follows the prototype and performs a POST, adding a _desired_ slug.
        // Investigate whether a PUT would be better (or worse)
        var fedoraUri = converters.GetFedoraUri(pathUnderFedoraRoot);
        
        var req = MakeHttpRequestMessage(fedoraUri.GetParentUri()!, HttpMethod.Post)
            .InTransaction(transaction)
            .WithName(name)
            .WithSlug(fedoraUri.GetSlug()!);
        if (asArchivalGroup)
        {
            req.AsArchivalGroup();
        }
        var response = await httpClient.SendAsync(req);
        response.EnsureSuccessStatusCode();

        if(asArchivalGroup)
        {
            // TODO - could combine this with .WithName(name) and make a more general .WithRdf(string name = null, string type = null)
            // so that the extra info goes on the initial POST and this little PATCH isn't required
            var patchReq = MakeHttpRequestMessage(response.Headers.Location!, HttpMethod.Patch)
                .InTransaction(transaction);
            // We give the AG this _additional_ type so that we can see that it's an AG when we get bulk child members back from .WithContainedDescriptions
            patchReq.AsInsertTypePatch("<http://purl.org/dc/dcmitype/Collection>");
            var patchResponse = await httpClient.SendAsync(patchReq);
            patchResponse.EnsureSuccessStatusCode();
        }
        // The body is the new resource URL
        var newReq = MakeHttpRequestMessage(response.Headers.Location!, HttpMethod.Get)
            .InTransaction(transaction)
            .ForJsonLd();
        var newResponse = await httpClient.SendAsync(newReq);

        var containerResponse = await MakeFedoraResponse<FedoraJsonLdResponse>(newResponse);
        if (containerResponse == null)
        {
            return null;
        }
        return asArchivalGroup ? converters.MakeArchivalGroup(containerResponse) : converters.MakeContainer(containerResponse);
    }

    private async Task<Container?> GetPopulatedContainer(Uri uri, bool isArchivalGroup, bool recurse, Transaction? transaction = null)
    {
        // TODO: This is not using transaction - should it?
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
    
    
    private static async Task<T?> MakeFedoraResponse<T>(HttpResponseMessage response) where T : FedoraJsonLdResponse
    {
        // works for SINGLE resources, not contained responses that send back a @graph
        var fedoraResponse = await response.Content.ReadFromJsonAsync<T>();
        if (fedoraResponse != null)
        {
            fedoraResponse.HttpResponseHeaders = response.Headers;
            fedoraResponse.HttpStatusCode = response.StatusCode;
            fedoraResponse.Body = await response.Content.ReadAsStringAsync();
        }
        return fedoraResponse;
    }
}