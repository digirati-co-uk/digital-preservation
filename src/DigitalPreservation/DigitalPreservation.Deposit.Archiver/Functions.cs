using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Workspace;
using Polly;
using Polly.Retry;
using Preservation.Client;
using Serilog;
using Checksum = DigitalPreservation.Utils.Checksum;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace DigitalPreservation.Deposit.Archiver;

public class Functions
{
    private readonly IPreservationApiClient preservationApiClient;
    private readonly WorkspaceManagerFactory workspaceManagerFactory;
    private readonly IIdentityMinter identityMinter;
    private readonly List<ArchiveDepositJob> archiveJobsList = [];

    private readonly AsyncRetryPolicy<Result<ArchiveJobResult>> retryArchiveDepositResult =
        Policy<Result<ArchiveJobResult>>
            .Handle<Exception>()
            .WaitAndRetryAsync(retryCount: 3,
                _ => TimeSpan.FromMilliseconds(3000));

    /// <summary>
    /// Default constructor that Lambda will invoke.
    /// </summary>
    public Functions(IPreservationApiClient preservationApiClient, WorkspaceManagerFactory workspaceManagerFactory, IIdentityMinter identityMinter)
    {
        this.preservationApiClient = preservationApiClient;
        this.workspaceManagerFactory = workspaceManagerFactory;
        this.identityMinter = identityMinter;
    }


    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [RestApi(LambdaHttpMethod.Get, "/")]
    public async Task<IHttpResult> Get(ILambdaContext context)
    {
        var query = BuildDepositQuery();

        var deposits = await preservationApiClient.GetDeposits(query, CancellationToken.None);

        if (!HasDeposits(deposits))
        {
            Log.Logger.Error(
                "No deposits returned {ErrorCode} {ErrorMessage}",
                deposits.ErrorCode,
                deposits.ErrorMessage);

            return HttpResults.Ok("No deposits to archive");
        }

        if (deposits.Value != null)
        {
            LogDeposits(deposits.Value.Deposits.Count);

            var batchContext = StartBatch();

            foreach (var deposit in deposits.Value.Deposits)
            {
                await ProcessDeposit(deposit);
            }

            await PersistArchiveJobs(batchContext);
        }

        ResetArchiveState();

        context.Logger.LogInformation("Handling the 'Get' Request");
        return HttpResults.Ok("Deposits archived");
    }

    private static DepositQuery BuildDepositQuery()
    {
        var rawMonthsValue = Convert.ToInt32(
            Environment.GetEnvironmentVariable("LAST_MODIFIED_MONTHS"));

        var months = Math.Abs(rawMonthsValue);

        if (months == 0)
        {
            throw new InvalidOperationException(
                "LAST_MODIFIED_MONTHS must be a non-zero value.");
        }

        var cutoffDate = DateTime.UtcNow.AddMonths(-1);

        return new DepositQuery
        {
            Status = "new",
            OrderBy = DepositQuery.LastModified,
            Page = 0,
            PageSize = Convert.ToInt32(Environment.GetEnvironmentVariable("BATCH_SIZE")),
            Ascending = true,
            ShowAll = false,
            Archived = false,
            LastModifiedBefore = cutoffDate,
            Active = false
        };
    }

    private static bool HasDeposits(Result<DepositQueryPage> deposits)
    {
        return deposits.Value?.Deposits is { Count: > 0 };
    }

    private static void LogDeposits(int count)
    {
        Log.Logger.Information("Got deposits for archiving from preservation API");
        Log.Logger.Information("Deposits count {DepositsCount}", count);
    }

    private BatchContext StartBatch()
    {
        return new BatchContext
        {
            StartTime = DateTime.UtcNow,
            BatchNumber = identityMinter.MintIdentity("ArchiveJob")
        };
    }

    private sealed record BatchContext
    {
        public DateTime StartTime { get; init; }
        public string BatchNumber { get; init; } = string.Empty;
    }

    private async Task ProcessDeposit(Common.Model.PreservationApi.Deposit deposit)
    {
        var depositId = deposit.Id?.Segments[^1];
        if (depositId is null)
            return;

        if (await HasPreviousFailedArchive(depositId))
            return;

        var workspaceManager = await GetWorkspaceManager(depositId, true);

        await ReleaseLock(deposit, depositId);

        await TryArchiveDeposit(deposit, depositId, workspaceManager);

        await TryPatchDeposit(deposit, depositId, workspaceManager);
    }

    private async Task<bool> HasPreviousFailedArchive(string depositId)
    {
        var previousJob =
            await preservationApiClient.GetArchiveJobResult(depositId, CancellationToken.None);

        if (previousJob is { Success: true, Value.Errors: not null } &&
            previousJob.Value.Errors.Length > 0)
        {
            Log.Logger.Information(
                "Previous archiver job run for this deposit {DepositId} had errors",
                depositId);
            return true;
        }

        return false;
    }

    private async Task ReleaseLock(Common.Model.PreservationApi.Deposit deposit, string depositId)
    {
        var result =
            await preservationApiClient.ReleaseDepositLock(deposit, CancellationToken.None);

        if (result.Failure)
        {
            Log.Logger.Information("issue releasing lock for {DepositId}", depositId);
        }
    }

    private async Task TryArchiveDeposit(
        Common.Model.PreservationApi.Deposit deposit,
        string depositId,
        Result<WorkspaceManager> workspaceManager)
    {
        if (workspaceManager.Value == null || deposit.Files == null)
            return;

        Log.Logger.Information("Calling archive for deposit {DepositId}", depositId);

        await Archive(workspaceManager.Value, depositId, deposit.Files);
    }

    private async Task TryPatchDeposit(
        Common.Model.PreservationApi.Deposit deposit,
        string depositId,
        Result<WorkspaceManager> workspaceManager)
    {
        var archivedDeposit = archiveJobsList.FirstOrDefault(x => x.DepositId == depositId);

        Log.Logger.Information(
            "Archived deposit is not null for deposit {ArchivedDeposit}. Deposit Uri: {DepositUri}",
            archivedDeposit != null,
            archivedDeposit?.DepositUri);

        if (!IsArchivableResult(archivedDeposit))
            return;

        deposit.Archived = DateTime.UtcNow;

        var patchResult =
            await preservationApiClient.UpdateDeposit(deposit, CancellationToken.None);

        if (patchResult.Failure)
        {
            await HandlePatchFailure(
                depositId,
                patchResult.ErrorMessage,
                workspaceManager,
                archivedDeposit);
            return;
        }

        Log.Logger.Information("Successfully patched deposit for {DepositId}", depositId);
    }

    private static bool IsArchivableResult(ArchiveDepositJob? job)
    {
        if (job == null)
            return false;

        if (string.IsNullOrEmpty(job.Errors))
            return true;

        return job.Errors.Contains("No items to delete.");
    }

    private static async Task HandlePatchFailure(
        string depositId,
        string? errorMessage,
        Result<WorkspaceManager> workspaceManager,
        ArchiveDepositJob? archivedDeposit)
    {
        Log.Logger.Error(
            "issue patching deposit for {DepositId} Error message: {ErrorMessage}",
            depositId,
            errorMessage);

        if (workspaceManager.Value != null)
        {
            var deleteResult =
                await DeleteDepositFiles(workspaceManager.Value);

            if (!deleteResult.Success)
            {
                Log.Logger.Error(
                    "Could not delete deposit files for {DepositId} Error message: {ErrorMessage}",
                    depositId,
                    deleteResult.ErrorMessage);
            }
        }

        if (archivedDeposit != null) archivedDeposit.Errors = errorMessage;
    }

    private async Task PersistArchiveJobs(BatchContext batch)
    {
        var endTime = DateTime.UtcNow;

        foreach (var job in archiveJobsList)
        {
            PopulateArchiveJob(job, batch, endTime);

            var result = await retryArchiveDepositResult.ExecuteAsync(() =>
                preservationApiClient.ArchiveDeposit(job, CancellationToken.None));

            if (result.Success)
            {
                Log.Logger.Information(
                    "Successfully archived deposit {DepositId} in batch {BatchNumber}",
                    job.DepositId,
                    batch.BatchNumber);
            }
            else
            {
                Log.Logger.Information(
                    "Issue archiving deposit {DepositId} in batch {BatchNumber} with error message {ErrorMessage}",
                    job.DepositId,
                    batch.BatchNumber,
                    result.ErrorMessage);
            }
        }
    }

    private void PopulateArchiveJob(
        ArchiveDepositJob job,
        BatchContext batch,
        DateTime endTime)
    {
        job.StartTime = batch.StartTime;
        job.EndTime = endTime;
        job.BatchNumber = batch.BatchNumber;
        job.Id = identityMinter.MintIdentity("ArchiveJobIdentifier");
    }

    private void ResetArchiveState()
    {
        archiveJobsList.Clear();
    }

    private async Task<Result<WorkspaceManager>> GetWorkspaceManager(
        string depositId,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        var response = await preservationApiClient.GetDeposit(depositId, cancellationToken);
        if (response.Failure || response.Value == null)
        {
            return Result.FailNotNull<WorkspaceManager>(response.ErrorCode ?? ErrorCodes.UnknownError,
                $"Could not archive deposit {depositId} as could not find the deposit.");
        }

        var deposit = response.Value;
        var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit, refresh);

        foreach (var warning in workspaceManager.Warnings)
        {
            Log.Logger.Warning(warning);
        }

        return Result.OkNotNull(workspaceManager);
    }
    private static async Task<Result<ItemsAffected>> DeleteDepositFiles(WorkspaceManager workspaceManager)
    {
        // This is an expensive operation (refresh=true):
        var root = await workspaceManager.RefreshCombinedDirectory();

        var (_, files) = root.Value!.Flatten();
        var deleteSelection = new DeleteSelection
        {
            DeleteFromDepositFiles = true,
            DeleteFromMets = false,
            Deposit = null,
            Items = []
        };

        var metadataPath = $"{FolderNames.Metadata}";
        var objectsPath = $"{FolderNames.Objects}";

        foreach (var file in files.Where(f => f.LocalPath!.StartsWith(metadataPath) || f.LocalPath!.StartsWith(objectsPath)))
        {
            deleteSelection.Items.Add(new MinimalItem
            {
                IsDirectory = false,
                RelativePath = file.LocalPath!,
                Whereabouts = Whereabouts.Both
            });
        }

        var resultDelete = await workspaceManager.DeleteItems(deleteSelection, "Deposit.Archiver");

        return resultDelete;
    }

    public async Task Archive(WorkspaceManager workspaceManager, string depositId, Uri depositUri)
    {
        var deleteFilesResult = await DeleteDepositFiles(workspaceManager);

        var archiveDepositJob = new ArchiveDepositJob
        {
            DepositId = depositId,
            DepositUri = depositUri.AbsoluteUri
        };

        if (deleteFilesResult.Success || (deleteFilesResult.ErrorMessage != null && deleteFilesResult.ErrorMessage.Contains("No items to delete.")))
        {
            Log.Logger.Information("Successfully deleted files for deposit {DepositId}", depositId);
            var markerFileUploadResult = await UploadMarkerFile(workspaceManager);

            if (!markerFileUploadResult.Success)
            {
                Log.Logger.Error("Errors uploading marker file for deposit {DepositId} Error message: {ErrorMessage}", depositId, markerFileUploadResult.ErrorMessage);
                archiveDepositJob.Errors += markerFileUploadResult.ErrorMessage;
            }
            else
            {
                Log.Logger.Information("Uploaded marker file for deposit {DepositId}", depositId);
                archiveDepositJob.DeletedCount = deleteFilesResult.Value?.Items.Count;
            }
        }

        if (deleteFilesResult.Failure)
        {
            archiveDepositJob.Errors = deleteFilesResult.ErrorMessage;
            Log.Logger.Information("Errors deleting files from deposit {DepositId}", depositId);
        }

        archiveJobsList.Add(archiveDepositJob);
    }

    private static async Task<Result<SingleFileUploadResult>> UploadMarkerFile(WorkspaceManager workspaceManager)
    {
        var tmpPath = Environment.GetEnvironmentVariable("TMP_FILES_PATH");
        var directorySeparator = Environment.GetEnvironmentVariable("DIRECTORY_SEPARATOR");

        if(tmpPath == null)
            return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError, "TMP_FILES_PATH environment variable is not set.");
        
        Directory.CreateDirectory(tmpPath);
        await File.WriteAllTextAsync($"{tmpPath}{directorySeparator}archived.txt", DateTime.UtcNow.ToString("s"), CancellationToken.None);

        Result<SingleFileUploadResult>? uploadMarkerFile;

        var fi = new FileInfo($"{tmpPath}{directorySeparator}archived.txt");
        var checksum = Checksum.Sha256FromFile(fi);

        await using (Stream stream = File.OpenRead($"{tmpPath}{directorySeparator}archived.txt"))
        {
            uploadMarkerFile = await workspaceManager.UploadSingleSmallFile(stream, stream.Length, "archived.txt",
                checksum!, "archived.txt", "text/plain", "", "archiver", true, false, true);
        }

        if (File.Exists($"{tmpPath}{directorySeparator}archived.txt"))
            File.Delete($"{tmpPath}{directorySeparator}archived.txt");

        return uploadMarkerFile;
    }

}
