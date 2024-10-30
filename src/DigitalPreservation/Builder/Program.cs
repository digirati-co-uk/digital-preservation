using System.Net.Http.Headers;
using System.Text;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Strings;
using IIIF.Serialisation;

namespace Builder;

class Program
{
    static async Task Main(string[] args)
    {
        var leedsColl = "https://presentation-api.dlip.digirati.io/2/collections";
        var demoColl = $"{leedsColl}/b7k0jd0e4qunf7y8272";
        var leedsManifests = "https://presentation-api.dlip.digirati.io/2/manifests";
        
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: Builder <manifest-title> <asset-space>");
            return;
        }
        var title = args[0];
        var space = Int32.Parse(args[1]);
        
        var dlcsUser = Environment.GetEnvironmentVariable("DLCS_USERNAME");
        var dlcsPassword = Environment.GetEnvironmentVariable("DLCS_PASSWORD");

        if (dlcsUser == null || dlcsPassword == null)
        {
            Console.WriteLine("Supply DLCS basic auth creds in environment variables <DLCS_USERNAME> <DLCS_PASSWORD>");
            return;
        }

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        var credentials = $"{dlcsUser}:{dlcsPassword}";
        var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
        httpClient.DefaultRequestHeaders.Add("X-IIIF-CS-Show-Extras", "All");
        
        var nqManifest = $"https://dlcs.dlip.digirati.io/iiif-resource/2/manifest/{space}";
        var json = await httpClient.GetStringAsync(nqManifest);
        var manifest = json.FromJson<Manifest>();
        manifest.Label = new LanguageMap("en", title);
        manifest.Id = null;
        int index = 1;
        foreach (var canvas in manifest.Items ?? [])
        {
            canvas.Label = new LanguageMap("en", "Canvas " + index++);
        }
        var manifestString = manifest.AsJson();
        
        // look away now 😱
        manifestString = manifestString.Replace(
            "\"type\": \"Manifest\"",
            $"\"type\": \"Manifest\", \"parent\": \"{demoColl}\", \"slug\": \"manifest-{space}\"");
        
        var resp = await httpClient.PostAsync(leedsManifests, new StringContent(manifestString, Encoding.UTF8, "application/json"));
        
        Console.WriteLine(resp.StatusCode);
    }
}