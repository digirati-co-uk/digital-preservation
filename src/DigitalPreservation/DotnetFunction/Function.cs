using Amazon;
using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using Preservation.Client;
using Serilog;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace DotnetFunction;

public class Function
{
    //private readonly IPreservationApiClient preservationApiClient;
    //private readonly WorkspaceManagerFactory workspaceManagerFactory;
    //private readonly IIdentityMinter identityMinter;
    private List<ArchiveDepositJob> archiveJobsList = [];
    private int deletedCount;
    private static IServiceProvider services;

    private readonly IServiceProvider _serviceProvider;

    private static readonly AsyncRetryPolicy<Result<ArchiveJobResult>> RetryArchiveDepositResult = Policy<Result<ArchiveJobResult>>
        .Handle<Exception>()
        .WaitAndRetryAsync(retryCount: 3, //Common.ConfigHelper.WelcomeEmail.MaxRetries
            _ => TimeSpan.FromMilliseconds(10000)); //Common.ConfigHelper.WelcomeEmail.RetryTimeout

    public static Func<IServiceCollection> ConfigureServices = () =>
    {
        var serviceCollection = new ServiceCollection();
        //serviceCollection.AddHttpClient("client", client =>
        //{
        //    client.BaseAddress = new Uri("someurl");
        //});

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false);

        var configuration = builder.Build();

        serviceCollection.ConfigureForwardedHeaders()
            .AddHttpContextAccessor()
            .AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<WorkspaceManagerFactory>();
            })
            .AddMachinePreservationClient(configuration, "ArchiverLambda", "https://preservation-api-dev.library.leeds.ac.uk");

        var fromServerlessTemplateoauthAzureSecret = "/preservation/dev/ui/oauth_azure";//Environment.GetEnvironmentVariable("OAUTH_AZURE_SECRET");
        var clientBaseAddress = Environment.GetEnvironmentVariable("CLIENT_BASE_ADDRESS");
        var secretJsonString = GetSecretValue(fromServerlessTemplateoauthAzureSecret!, "eu-west-1");

        var secretModel = System.Text.Json.JsonSerializer.Deserialize<AuthProviderModel>(secretJsonString);

        var accessTokenProviderOptions = new AccessTokenProviderOptions
        {
            ClientId = secretModel?.ClientId,
            ClientSecret = secretModel?.ClientSecret,
            TenantId = secretModel?.TenantId
        };
        serviceCollection.AddSingleton<IAccessTokenProviderOptions>(accessTokenProviderOptions);
        serviceCollection.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();

        //var builder = new ConfigurationBuilder()
        //    .SetBasePath(Directory.GetCurrentDirectory())
        //    .AddJsonFile("appsettings.json", false);

        //serviceCollection.AddTransient<IPreservationApiClient, PreservationApiClient>();


        services = serviceCollection.BuildServiceProvider();
        return serviceCollection;
    };

   
    IServiceCollection serviceCollection;
    public Function()
    {
        //var serviceCollection = new ServiceCollection();
        //services = Startup.ConfigureServices(serviceCollection);
        serviceCollection = ConfigureServices();


 //clientBaseAddress
    }
    /// <summary>
    /// Default constructor that Lambda will invoke.
    /// </summary>
    //public Function(IPreservationApiClient preservationApiClient, WorkspaceManagerFactory workspaceManagerFactory, IIdentityMinter identityMinter)
    //{
    //    this.preservationApiClient = preservationApiClient;
    //    this.workspaceManagerFactory = workspaceManagerFactory;
    //    this.identityMinter = identityMinter;
    //}
    //public Function(IServiceProvider serviceProvider)
    //{
    //    _serviceProvider = serviceProvider;
    //}
    /// <summary>
    /// A simple function that takes a string and returns both the upper and lower case version of the string.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    //[LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<Dictionary<string, dynamic>> FunctionHandler(ILambdaContext context)
    {
        //var t = serviceCollection
        string name = "bm";
        string message = string.Format("Hello, {0}!", name);
        var preservationApiClient = services.GetService<IPreservationApiClient>();

        var lastModifiedBefore = -Convert.ToInt32(Environment.GetEnvironmentVariable("LAST_MODIFIED_MONTHS"));
        var query = new DepositQuery
        {
            Status = "preserved",
            OrderBy = DepositQuery.LastModified,
            Page = 0,
            PageSize = 25, //Convert.ToInt32(Environment.GetEnvironmentVariable("BATCH_SIZE")),
            Ascending = true,
            ShowAll = true,
            Archived = false,
            LastModifiedBefore = DateTime.UtcNow.AddMonths(6)
        };

        var deposits = await preservationApiClient.GetDeposits(query, CancellationToken.None);
       

        return new Dictionary<string, dynamic>
        {
            { "statusCode", 200 },
            { "body", deposits.Value!.Deposits.Count }
        };

        //var lastModifiedBefore = -Convert.ToInt32(Environment.GetEnvironmentVariable("LAST_MODIFIED_MONTHS"));
        //var query = new DepositQuery
        //{
        //    Status = "preserved",
        //    OrderBy = DepositQuery.LastModified,
        //    Page = 0,
        //    PageSize = 25, //Convert.ToInt32(Environment.GetEnvironmentVariable("BATCH_SIZE")),
        //    Ascending = true,
        //    ShowAll = true,
        //    Archived = false,
        //    LastModifiedBefore = DateTime.UtcNow.AddMonths(6)
        //};

        //var deposits = await preservationApiClient.GetDeposits(query, CancellationToken.None);

        //if (deposits.Value != null && deposits.Value.Deposits.Count > 0)
        //{
        //    Log.Logger.Information("Got deposits for archiving from preservation API");
        //    Log.Logger.Information("Deposits count {depositsCount}", deposits.Value?.Deposits.Count);
        //}
        //else
        //{
        //    Log.Logger.Error("No deposits returned {errorCode} {errorMessage}", deposits.ErrorCode, deposits.ErrorMessage);
        //}

        //var startTime = DateTime.UtcNow;
        //var batchNumber = identityMinter.MintIdentity("ArchiveJob");

        //if (deposits.Value?.Deposits is { Count: > 0 })
        //{
        //    foreach (var deposit in deposits.Value?.Deposits!)
        //    {
        //        var depositId = deposit.Id?.Segments[^1];
        //        if (depositId is null) continue;
        //        var workspaceManager = await GetWorkspaceManager(depositId, true);
        //        var releaseLock = await preservationApiClient.ReleaseDepositLock(deposit, CancellationToken.None);

        //        //check if in deposit archiver jobs table with errors - dont try again
        //        var previousDepositArchiverJob = await preservationApiClient.GetArchiveJobResult(depositId, CancellationToken.None);

        //        if (previousDepositArchiverJob is { Success: true, Value.Errors: not null } && previousDepositArchiverJob.Value.Errors.Any())
        //        {
        //            Log.Logger.Information("Previous archiver job run for this deposit {depositId} had errors", depositId);
        //            continue;
        //        }

        //        if (releaseLock.Failure)
        //            Log.Logger.Information("issue releasing lock for {depositId}", depositId);

        //        if (workspaceManager.Value != null && deposit.Files != null)
        //        {
        //            Log.Logger.Information("Calling archive for deposit {depositId}", depositId);
        //            await Archive(workspaceManager.Value, depositId, deposit.Files);
        //        }

        //        var archivedDeposit = archiveJobsList.FirstOrDefault(x => x.DepositId == depositId);
        //        Log.Logger.Information("Archived deposit is not null for deposit {archivedDeposit}. Deposit Uri: {depositUri}", archivedDeposit != null, archivedDeposit?.DepositUri);

        //        if (archivedDeposit == null || (!string.IsNullOrEmpty(archivedDeposit.Errors) && !archivedDeposit.Errors.Contains("No items to delete."))) continue;

        //        Log.Logger.Information("No errors and Items to delete for deposit id {depositId}", depositId);
        //        deposit.Archived = DateTime.UtcNow;

        //        var patchDeposit = await preservationApiClient.UpdateDeposit(deposit, CancellationToken.None);

        //        if (patchDeposit.Failure)
        //        {
        //            Log.Logger.Error("issue patching deposit for {depositId} Error message: {errorMessage}", depositId, patchDeposit.ErrorMessage);

        //            if (workspaceManager.Value != null)
        //            {
        //                var deleteFilesResult = await DeleteDepositFiles(workspaceManager.Value);

        //                if (!deleteFilesResult.Success)
        //                    Log.Logger.Error("Could not delete archived.txt file for {depositId} Error message: {errorMessage}", depositId, deleteFilesResult.ErrorMessage);
        //            }

        //            var index = archiveJobsList.IndexOf(archivedDeposit);
        //            if (index > -1)
        //            {
        //                archiveJobsList[index].Errors = patchDeposit.ErrorMessage;
        //            }
        //        }


        //        if (patchDeposit.Success)
        //            Log.Logger.Error("Successfully patched deposit for {depositId}", depositId);
        //    }
        //}

        //var endTime = DateTime.UtcNow;

        //foreach (var archiveJob in archiveJobsList)
        //{
        //    archiveJob.StartTime = startTime;
        //    archiveJob.EndTime = endTime;
        //    archiveJob.BatchNumber = batchNumber;
        //    archiveJob.Id = identityMinter.MintIdentity("ArchiveJobIdentifier");
        //    archiveJob.DeletedCount = deletedCount;

        //    var archiveJobResult = await RetryArchiveDepositResult.ExecuteAsync(() =>
        //        preservationApiClient.ArchiveDeposit(archiveJob, CancellationToken.None));

        //    if (archiveJobResult.Success)
        //        Log.Logger.Information("Successfully archived deposit {depositId} in batch {batchNumber}", archiveJob.DepositId, batchNumber);

        //    if (archiveJobResult.Failure)
        //        Log.Logger.Information("Issue archiving deposit {depositId} in batch {batchNumber} with error message {errorMessage}", archiveJob.DepositId, batchNumber, archiveJobResult.ErrorMessage);

        //}

        ////re-initialise
        //deletedCount = 0;
        //archiveJobsList.Clear();
        //context.Logger.LogInformation("Handling the 'Get' Request");

        return new Dictionary<string, dynamic>
        {
            { "statusCode", 200 },
            { "body", "Deposit archived" }
        };
    }

    ///// <summary>
    ///// A simple function that takes a string and returns both the upper and lower case version of the string.
    ///// </summary>
    ///// <param name="input">The event for the Lambda function handler to process.</param>
    ///// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    ///// <returns></returns>
    //public Casing FunctionHandler(string input, ILambdaContext context)
    //{
    //    return new Casing(input.ToLower(), input.ToUpper());
    //}

    public async Task Archive(WorkspaceManager workspaceManager, string depositId, Uri depositUri)
    {
        var deleteFilesResult = await DeleteDepositFiles(workspaceManager);

        var archiveDepositJob = new ArchiveDepositJob
        {
            DepositId = depositId,
            DepositUri = depositUri.AbsoluteUri,
        };

        if (deleteFilesResult.Success || (deleteFilesResult.ErrorMessage != null && deleteFilesResult.ErrorMessage.Contains("No items to delete.")))
        {
            Log.Logger.Information("Successfully deleted files for deposit {depositId}", depositId);
            var markerFileUploadResult = await UploadMarkerFile(workspaceManager);

            if (!markerFileUploadResult.Success)
            {
                Log.Logger.Error("Errors uploading marker file for deposit {depositId} Error message: {errorMessage}", depositId, markerFileUploadResult.ErrorMessage);
                archiveDepositJob.Errors += markerFileUploadResult.ErrorMessage;
            }
            else
            {
                Log.Logger.Information("Uploaded marker file for deposit {depositId}", depositId);
                deletedCount += 1;
            }
        }

        if (deleteFilesResult.Failure)
        {
            archiveDepositJob.Errors = deleteFilesResult.ErrorMessage;
            Log.Logger.Information("Errors deleting files from deposit {depositId}", depositId);
        }


        archiveJobsList.Add(archiveDepositJob);
    }

    private async Task<Result<SingleFileUploadResult>> UploadMarkerFile(WorkspaceManager workspaceManager)
    {
        var tmpPath = Environment.GetEnvironmentVariable("TMP_FILES_PATH");
        var directorySeparator = Environment.GetEnvironmentVariable("DIRECTORY_SEPARATOR");

        Directory.CreateDirectory(tmpPath);
        await File.WriteAllTextAsync($"{tmpPath}{directorySeparator}archived.txt", DateTime.UtcNow.ToString("s"), CancellationToken.None);

        Result<SingleFileUploadResult>? uploadMarkerFile;

        var fi = new FileInfo($"{tmpPath}{directorySeparator}archived.txt");
        var checksum = Checksum.Sha256FromFile(fi);

        await using (Stream stream = File.OpenRead($"{tmpPath}{directorySeparator}archived.txt"))
        {
            uploadMarkerFile = await workspaceManager.UploadSingleSmallFile(stream, stream.Length, "archived.txt",
                checksum!, "archived.txt", "text/plain", "", "archiver", true, true);
        }

        if (File.Exists($"{tmpPath}{directorySeparator}archived.txt"))
            File.Delete($"{tmpPath}{directorySeparator}archived.txt");

        return uploadMarkerFile;
    }

    private async Task<Result<ItemsAffected>> DeleteDepositFiles(WorkspaceManager workspaceManager)
    {
        // This is an expensive operation (refresh=true):
        var root = await workspaceManager.RefreshCombinedDirectory();

        var (_, files) = root.Value!.Flatten();
        var deleteSelection = new DeleteSelection
        {
            DeleteFromDepositFiles = true,
            DeleteFromMets = false,
            Deposit = null,
            Items = [],
            NonDeletableRootFiles = ["mets.xml", "__METSlike.json"]
        };

        var metadataPath = $"{FolderNames.Metadata}";
        var objectsPath = $"{FolderNames.Objects}";

        foreach (var file in files)
        {
            if (file.LocalPath!.StartsWith(metadataPath) || file.LocalPath!.StartsWith(objectsPath) || !deleteSelection.NonDeletableRootFiles.Contains(file.LocalPath))
            {
                deleteSelection.Items.Add(new MinimalItem
                {
                    IsDirectory = false,
                    RelativePath = file.LocalPath,
                    Whereabouts = Whereabouts.Both
                });
            }
        }

        var resultDelete = await workspaceManager.DeleteItems(deleteSelection, "Deposit.Archiver");

        return resultDelete;
    }

    //private async Task<Result<WorkspaceManager>> GetWorkspaceManager(
    //    string depositId,
    //    bool refresh,
    //    CancellationToken cancellationToken = default)
    //{
    //    var response = await preservationApiClient.GetDeposit(depositId, cancellationToken);
    //    if (response.Failure || response.Value == null)
    //    {
    //        return Result.FailNotNull<WorkspaceManager>(response.ErrorCode ?? ErrorCodes.UnknownError,
    //            $"Could not archive deposit {depositId} as could not find the deposit.");
    //    }

    //    var deposit = response.Value;
    //    var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit, refresh);

    //    foreach (var warning in workspaceManager.Warnings)
    //    {
    //        Log.Logger.Warning(warning);
    //    }

    //    return Result.OkNotNull(workspaceManager);
    //}

    private static string GetSecretValue(string secretName, string region)
    {
        var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));

        var request = new GetSecretValueRequest()
        {
            SecretId = secretName,
            VersionStage = "AWSCURRENT"
        };

        var response = client.GetSecretValueAsync(request).GetAwaiter().GetResult();
        return response.SecretString;
    }
}
