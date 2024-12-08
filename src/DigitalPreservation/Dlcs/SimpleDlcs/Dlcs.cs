using System.Net;
using Dlcs.Hydra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dlcs.SimpleDlcs;

public class Dlcs : IDlcs
{
    private readonly ILogger<Dlcs> logger;
    private readonly HttpClient httpClient;
    private readonly DlcsOptions options;


    public Dlcs(
        ILogger<Dlcs> logger,
        IOptions<DlcsOptions> options,
        HttpClient httpClient)
    {
        this.logger = logger;
        this.httpClient = httpClient;
        this.options = options.Value;
    }
    
    private static Uri? _imageQueueUri;
    private void InitQueue()
    {
        if (_imageQueueUri == null)
        {
            // TODO: At this point we would work out RESTfully where the queue for this customer is
            // and cache that for a bit - we assume the API stays reasonably stable.
            _imageQueueUri = new Uri($"{options.ApiEntryPoint}customers/{options.CustomerId}/queue");
        }
    }

    /*
     * NO ERROR HANDLING!
     * Wellcome Dlcs client has Operation wrapper for logging request bodies, adding correlation headers etc.
     * Eventual generic Dlcs client should have same
     */


    public async Task<Batch> RegisterImages(HydraImageCollection images)
    {
        InitQueue();
        var response = await httpClient.PostAsJsonAsync(_imageQueueUri, images);
        response.EnsureSuccessStatusCode();
        var batch = await response.Content.ReadFromJsonAsync<Batch>();
        return batch!;
    }

    public async Task<Batch> GetBatch(string batchId)
    {
        const string batchTemplate = "{0}customers/{1}/queue/batches/{2}";
        if (!Uri.IsWellFormedUriString(batchId, UriKind.Absolute))
        {
            batchId = string.Format(batchTemplate, options.ApiEntryPoint, options.CustomerId, batchId);
        }
        var batch = await httpClient.GetFromJsonAsync<Batch>(batchId);
        return batch!;
    }

    public Task<HydraImageCollection> PatchImages(HydraImageCollection images)
    {
        throw new NotImplementedException();
    }

    public Task<Image?> GetImage(int space, string id)
    {
        throw new NotImplementedException();
    }

    public async Task<HydraImageCollection> GetFirstPageOfImages(ImageQuery query, int defaultSpace)
    {
        int space = defaultSpace;
        if (query.Space.HasValue) space = query.Space.Value;
        var imageQueryUri = $"{options.ApiEntryPoint}customers/{options.CustomerId}/spaces/{space}/images";
        var uriBuilder = new UriBuilder(imageQueryUri)
        {
            Query = $"?q={JsonSerializer.Serialize(query)}"
        };
        var images = await httpClient.GetFromJsonAsync<HydraImageCollection>(uriBuilder.Uri);
        return images!;
    }

    public async Task<HydraImageCollection> GetPageOfImages(Uri nextUri)
    {
        var images = await httpClient.GetFromJsonAsync<HydraImageCollection>(nextUri);
        return images!;
    }

    public async Task<IEnumerable<Image>> GetImagesFromQuery(ImageQuery query)
    {
        bool first = true;
        Uri? nextUri = null;

        var images = new List<Image>();
        while (first || nextUri != null)
        {
            HydraImageCollection? page;
            if (first)
            {
                page = await GetFirstPageOfImages(query, options.CustomerDefaultSpace);
                first = false;
            }
            else
            {
                page = await GetPageOfImages(nextUri!);
            }
            if (page.Members != null)
            {
                images.AddRange(page.Members);
            }
            if (page.View != null && page.View.Next != null)
            {
                nextUri = new Uri(page.View.Next); // Make this a Uri on the Hydra class
            }
            else
            {
                nextUri = null;
            }
        }

        return images;
    }

    public async Task<Dictionary<string, long>> GetDlcsQueueLevel()
    {
        var url = $"{options.ApiEntryPoint}queue";
        var response = await httpClient.GetStringAsync(url);
        var result = JsonDocument.Parse(response).RootElement;
        return new Dictionary<string, long>
        {
            {"incoming", result.GetProperty("incoming").GetInt64()},
            {"priority", result.GetProperty("priority").GetInt64()}
        };
    }

    public async Task<bool> SpaceExists(int space)
    {
        var spaceUri = $"{options.ApiEntryPoint}customers/{options.CustomerId}/spaces/{space}";
        var resp = await httpClient.GetAsync(spaceUri);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            return true;
        }

        return true; // for demo's sake
    }

    public async Task CreateSpace(int space, string? name = null)
    {
        name ??= "Space " + space;
        var spaceUri = $"{options.ApiEntryPoint}customers/{options.CustomerId}/spaces/{space}";
        var resp = await httpClient.PutAsJsonAsync(spaceUri, new Space{ Name = name });
        if (resp.StatusCode != HttpStatusCode.Created)
        {
            throw new NotImplementedException();
        }
    }
}
