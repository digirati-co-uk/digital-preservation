using System.Net.Http.Headers;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using Dlcs;
using Dlcs.Hydra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.Client;

namespace Registrant;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: Registrant <archival-group> <target-space>");
            return;
        }

        var archivalGroupPath = args[0];
        var requestedSpace = Int32.Parse(args[1]);

        var dlcsUser = Environment.GetEnvironmentVariable("DLCS_USERNAME");
        var dlcsPassword = Environment.GetEnvironmentVariable("DLCS_PASSWORD");

        if (dlcsUser == null || dlcsPassword == null)
        {
            Console.WriteLine("Supply DLCS basic auth creds in environment variables <DLCS_USERNAME> <DLCS_PASSWORD>");
            return;
        }
        
        IOptions<DlcsOptions> dlcsOptions = Options.Create(
            new DlcsOptions
            {
                ApiEntryPoint = "https://api.dlip.digirati.io/",
                CustomerId = 2,
                CustomerDefaultSpace = -1
            });

        var dlcs = new Dlcs.SimpleDlcs.Dlcs(
            GetLogger<Dlcs.SimpleDlcs.Dlcs>(),
            dlcsOptions,
            GetHttpClient(dlcsOptions.Value.ApiEntryPoint!, dlcsUser, dlcsPassword));

        //var validatedSpace = requestedSpace;
        var validatedSpace = await EnsureSpace(dlcs, requestedSpace);
        if (validatedSpace <= 0)
        {
            Console.WriteLine("Space {0} has already been taken", requestedSpace);
            return;
        }

        var storageApi = new StorageApiClient(
            GetHttpClient("https://storage-dev.dlip.digirati.io"),
            GetLogger<StorageApiClient>());
        var archivalGroupResult = await storageApi.GetArchivalGroup(archivalGroupPath);
        if (archivalGroupResult.Failure || archivalGroupResult.Value == null)
        {
            Console.WriteLine("Archive group " + archivalGroupPath + " could not be found.");
            return;
        }

        var archivalGroup = archivalGroupResult.Value;

        var preservedImages = GetFlattenedImageAssets(archivalGroup);
        List<Image> imagesToRegister = [];
        int sequenceIndex = 1;
        foreach (var binary in preservedImages)
        {
            var s3Uri = new AmazonS3Uri(binary.Origin);
            var string1 = archivalGroupPath.Remove(0, "repository/".Length);
            var string2 = binary.Id!.AbsolutePath.TrimStart('/').Remove(0, archivalGroupPath.Length);
            imagesToRegister.Add(new Image
            {
                ModelId = string2.Replace("/", "__"),
                Space = validatedSpace,
                String1 = string1,
                String2 = string2,
                Number1 = sequenceIndex,
                MediaType = binary.ContentType,
                Origin = $"https://{s3Uri.Bucket}.s3.amazonaws.com/{s3Uri.Key}"
            });
            sequenceIndex++;
        }

        var hydraCollection = new HydraImageCollection { Members = imagesToRegister.ToArray() };
        var batch = await dlcs.RegisterImages(hydraCollection);

        Console.WriteLine("Hydra images registered");
    }

   
    private static async Task<int> EnsureSpace(IDlcs dlcs, int requestedSpace)
    {
         bool exists = await dlcs.SpaceExists(requestedSpace);
         if (exists)
         {
             return -1;
         }
         await dlcs.CreateSpace(requestedSpace);
         return requestedSpace;
    }

    private static List<Binary> GetFlattenedImageAssets(ArchivalGroup ag)
    {
        var images = new List<Binary>();
        AddImagesFromContainer(images, ag);
        return images;
    }

    private static void AddImagesFromContainer(List<Binary> images, Container container)
    {
        images.AddRange(container.Binaries
            .Where(b => b.ContentType != null && b.ContentType.StartsWith("image/"))
            .OrderBy(b => b.GetSlug()));
        foreach (Container c in container.Containers.OrderBy(c => c.GetSlug()))
        {
            AddImagesFromContainer(images, c);
        }
    }


    private static ILogger<T> GetLogger<T>()
    {
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger<T> logger = factory.CreateLogger<T>();
        return logger;
    }

    private static HttpClient GetHttpClient(string baseAddress, string? basicUser = null, string? basicPwd = null)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(100)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (basicUser != null)
        {
            var credentials = $"{basicUser}:{basicPwd}";
            var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
        }
        return httpClient;
    }
}