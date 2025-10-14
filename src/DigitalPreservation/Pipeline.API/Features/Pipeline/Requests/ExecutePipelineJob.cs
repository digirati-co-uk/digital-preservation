using Azure.Core;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Preservation.Client;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Timers;
using Checksum = DigitalPreservation.Utils.Checksum;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ExecutePipelineJob(string jobIdentifier, string depositId, string? runUser) : IRequest<Result>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string DepositId { get; } = depositId;
    private string? RunUser { get; } = runUser;
    public string GetUserName() => RunUser ?? "PipelineApi";
}


public class ProcessPipelineJobHandler(
    ILogger<ProcessPipelineJobHandler> logger,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions,
    WorkspaceManagerFactory workspaceManagerFactory,
    IPreservationApiClient preservationApiClient) : IRequestHandler<ExecutePipelineJob, Result>

{
    private const string BrunnhildeFolderName = "brunnhilde";
    private readonly string[] filesToIgnore = ["tree.txt"];
    private StreamReader? _streamReader;

    private int _processId;
    private readonly System.Timers.Timer ProcessTimer = new(10000);

    /// <summary>
    /// Reacquiring a new WorkspaceManager is not expensive, but refreshing the file system is
    /// (e.g., GetCombinedDirectory(true))
    /// </summary>
    /// <param name="request">The Mediatr request</param>
    /// <param name="refresh">Whether to rebuild the CombinedDirectory in the Workspace</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<Result<WorkspaceManager>> GetWorkspaceManager(
        ExecutePipelineJob request, 
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        var response = await preservationApiClient.GetDeposit(request.DepositId, cancellationToken);
        if (response.Failure || response.Value == null)
        {
            return Result.FailNotNull<WorkspaceManager>(response.ErrorCode ?? ErrorCodes.UnknownError,
                $"Could not process pipeline job for job id {request.JobIdentifier} and deposit {request.DepositId} as could not find the deposit.");
        }

        var deposit = response.Value;
        var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit, refresh);
        foreach (var warning in workspaceManager.Warnings)
        {
            logger.LogWarning(warning);
        }

        return Result.OkNotNull(workspaceManager);
    }

    private async Task<Result<LogPipelineStatusResult>> UpdateJobStatus(
        ExecutePipelineJob request, string status, CancellationToken cancellationToken = default)
    {
        var result = await UpdateJobStatus(request, status, (string?)null, cancellationToken);
        return result;
    }
    
    private async Task<Result<LogPipelineStatusResult>> UpdateJobStatus(
        ExecutePipelineJob request, string status, Error[]? errors, CancellationToken cancellationToken = default)
    {
        string? error = null;
        if (errors is { Length: > 0 })
        {
            error = string.Join(Environment.NewLine, errors.Select(e => e.Message));
        }
        var result = await UpdateJobStatus(request, status, error,  cancellationToken);
        return result;
    }
    
    private async Task<Result<LogPipelineStatusResult>> UpdateJobStatus(
        ExecutePipelineJob request, string status, string? errors = null, CancellationToken cancellationToken = default)
    {
        var result = await UpdateAnyJobStatus(
            request.DepositId, request.JobIdentifier, status, request.GetUserName(), errors, cancellationToken);
        return result;
    }

    private async Task<Result<LogPipelineStatusResult>> UpdateAnyJobStatus(
        string depositId, string jobId, string status, string runUser, string? errors = null, CancellationToken cancellationToken = default)
    {
        // Do this directly not by chaining mediatr
        var pipelineDeposit = new PipelineDeposit
        {
            Id = jobId,
            Status = status,
            DepositId = depositId,
            RunUser = runUser,
            Errors = errors
        };
        var updateResult = await preservationApiClient.LogPipelineRunStatus(pipelineDeposit, cancellationToken);

        if (updateResult is { Success: true, Value: not null } && errors.IsNullOrWhiteSpace())
        {
            logger.LogInformation("Job {jobIdentifier} status updated: {status}", jobId, status);
        }
        else if (updateResult is { Success: true } && errors.HasText())
        {
            logger.LogError("Updated pipeline job {jobIdentifier} to status: {status}, recording errors {errors}", 
                jobId, status, errors);
        }
        else
        {
            logger.LogError("Failed to update job {jobIdentifier} status: {status}; {error}, was trying to log errors: {errors}", 
                jobId, status, updateResult.CodeAndMessage(), errors);
        }
        return updateResult;
    }
    
    
    public async Task<Result> Handle(ExecutePipelineJob request, CancellationToken cancellationToken)
    {
        //await CleanupPipelineRunsForDeposit(request);
        await UpdateJobStatus(request, PipelineJobStates.Running, cancellationToken);

        var workspaceResult = await GetWorkspaceManager(request, true, cancellationToken);
        if (workspaceResult.Failure || workspaceResult.Value?.Deposit == null)
        {
            return Result.Fail(workspaceResult.ErrorCode ?? ErrorCodes.UnknownError,
                $"Could not process pipeline job for job id {request.JobIdentifier} and deposit {request.DepositId} as could not find the deposit.");
        }

        var workspace = workspaceResult.Value;

        try
        {
            var (forceComplete, _) = await CheckIfForceComplete(request, workspace.Deposit, cancellationToken);
            if (forceComplete)
            {
                logger.LogInformation("Pipeline job run {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                
                return Result.FailNotNull<Result>(ErrorCodes.UnknownError,
                    $"Pipeline job run {request.JobIdentifier} for deposit {request.DepositId} has been force completed.");
            }

            logger.LogInformation("About to execute Brunnhilde for pipeline job run {JobIdentifier} for deposit {DepositId}", request.JobIdentifier, request.DepositId);
            var result = await ExecuteBrunnhilde(request, workspace, cancellationToken);

            if(!result.CleanupProcessJob)
                await UpdateJobStatus(request, result.Status, result.Errors, cancellationToken);

            logger.LogInformation("Execute Brunnhilde result test {status} {errors} ", result.Status, result.Errors);
            return result.Status == PipelineJobStates.Completed
                ? Result.Ok()
                : Result.FailNotNull<Result>(ErrorCodes.UnknownError,
                    $"Could not complete pipeline run {result.Errors?.FirstOrDefault()?.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Caught error in PipelineJob handler for job id {jobIdentifier} and deposit {depositId}",
                request.JobIdentifier, request.DepositId);

            await TryReleaseLock(request, workspace.Deposit, cancellationToken);

            var pipelineJobsResult = await UpdateJobStatus(
                request, PipelineJobStates.CompletedWithErrors, ex.Message, cancellationToken);

            if (pipelineJobsResult.Value?.Errors is { Length: 0 })
                logger.LogInformation("Job {jobIdentifier} Running status CompletedWithErrors logged",
                    request.JobIdentifier);

            return Result.FailNotNull<Result>(ErrorCodes.UnknownError,
                $"Could not publish pipeline job for job id {request.JobIdentifier} and deposit {request.DepositId}: " +
                ex.Message);
        }
        finally
        {
            CleanupProcessFolder(request.DepositId);
        }
    }

    private void CleanupProcessFolder(string depositName)
    {
        var processFolder = brunnhildeOptions.Value.ProcessFolder;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var metadataPathForProcessDelete = $"{processFolder}{separator}{depositName}";
        Directory.Delete(metadataPathForProcessDelete, true);
    }

    private async Task<ProcessPipelineResult> ExecuteBrunnhilde(ExecutePipelineJob request,
        WorkspaceManager workspaceManager, CancellationToken cancellationToken)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;

        if (!Directory.Exists(mountPath))
        {
            logger.LogError("S3 mount path could not be found at {mountPath}", mountPath);
            await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors = [new Error { Message = $"S3 mount path could not be found at {mountPath}" }]
            };
        }

        var (metadataPath, metadataProcessPath, objectPath) = GetFilePaths(workspaceManager);
        if (!Directory.Exists(objectPath))
        {
            var errorMessage = $"Could not find object folder for deposit {request.DepositId}";
            logger.LogError("Deposit {depositId} folder and contents could not be found at {objectPath}", 
                request.DepositId, objectPath);
            var releaseLockResult1 = await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
            if (releaseLockResult1.Failure)
            {
                errorMessage += " and could not unlock";
            }
            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors = [new Error { Message = errorMessage }]
            };
        }

        logger.LogInformation("Metadata folder value: {metadataPath}", metadataPath);
        logger.LogInformation("Metadata process folder value: {metadataProcessPath}", metadataProcessPath);
        logger.LogInformation("Object folder path value: {objectPath}", objectPath);

        var (forceComplete, cleanupProcessJob) = await CheckIfForceComplete(request, workspaceManager.Deposit, cancellationToken);

        if (forceComplete)
        {
            await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
            if (!cleanupProcessJob)
            {
                logger.LogInformation("Exited as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} was force completed" }]
                };
            }

            logger.LogInformation("Exited as the pipeline job run has been cleaned up as previous processing did not complete {JobIdentifier} for deposit {DepositId}.", request.JobIdentifier, request.DepositId);
            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors = [new Error { Message = "Cleaned up as previous processing did not complete" }],
                CleanupProcessJob = true
            }; 
        }

        try
        {
            //var runProcessResult = await RunProcessAsync(objectPath, metadataProcessPath, cancellationToken);

            using var process = new Process();
            process.StartInfo.FileName = brunnhildeOptions.Value.PathToPython;
            process.StartInfo.Arguments = $"  {brunnhildeOptions.Value.PathToBrunnhilde} --hash sha256 {objectPath} {metadataProcessPath}  --overwrite ";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;

            logger.LogInformation("Brunnhilde process about to be started {date}", DateTime.Now);
            var started = process.Start();


            if (started)
            {
                logger.LogInformation("Brunnhilde process started {date}", DateTime.Now);
                _processId = process.Id;
            }

            //var processTimer = new System.Timers.Timer(3000);
            ProcessTimer.Elapsed += (sender, e) => CheckIfProcessRunning(sender, e, request, workspaceManager.Deposit, cancellationToken);
            //processTimer.Elapsed += CheckIfProcessRunning;
            ProcessTimer.AutoReset = true;
            ProcessTimer.Enabled = true;

            _streamReader = process?.StandardOutput;

        }
        catch (Exception e)
        {
            logger.LogError(e, "Error thrown running the process and timer");
            var s = e.Message;
        }

        if (_streamReader == null)
        {
            logger.LogError("Steam reader is null Issue executing Brunnhilde process: process?.StandardOutput is null");

            logger.LogError("Caught error in PipelineJob handler for job id {jobIdentifier} and deposit {depositId}",
                request.JobIdentifier, request.DepositId);

            await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);

            var (forceCompleteStreamReader, cleanupProcessJobStreamReader) = await CheckIfForceComplete(request, workspaceManager.Deposit, cancellationToken);
            if (forceCompleteStreamReader)
            {
                //await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
                if (!cleanupProcessJobStreamReader)
                {
                    logger.LogInformation("Exited as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                    return new ProcessPipelineResult
                    {
                        Status = PipelineJobStates.CompletedWithErrors,
                        Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} was force completed" }]
                    };
                }

                logger.LogInformation("Exited as the pipeline job run has been cleaned up as previous processing did not complete {JobIdentifier} for deposit {DepositId}.", request.JobIdentifier, request.DepositId);
                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors = [new Error { Message = "Cleaned up as previous processing did not complete" }],
                    CleanupProcessJob = true
                };
            }

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} has issue with Brunnhilde output stream reader being null" }]
            };
        }

        logger.LogInformation("_streamReader {streamReader}",_streamReader);
        var result = await _streamReader.ReadToEndAsync(CancellationToken.None);
        _streamReader = null;
        var brunnhildeExecutionSuccess = result.Contains("Brunnhilde characterization complete.");
        logger.LogInformation("Brunnhilde result success: {brunnhildeExecutionSuccess}", brunnhildeExecutionSuccess);

        if (brunnhildeExecutionSuccess)
        {
            logger.LogInformation("Brunnhilde creation successful");
            var depositPath = $"{mountPath}{separator}{request.DepositId}";

            var (forceCompleteOnSuccess, cleanupProcessJobOnSuccess) = await CheckIfForceComplete(request, workspaceManager.Deposit, cancellationToken);
            // At this point we have not modified the METS file, the ETag for this workspace is still valid
            if (forceCompleteOnSuccess)
            {
                await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
                if (!cleanupProcessJobOnSuccess)
                {
                    logger.LogInformation("Exited as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                    return new ProcessPipelineResult
                    {
                        Status = PipelineJobStates.CompletedWithErrors,
                        Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} was force completed" }]
                    };
                }

                logger.LogInformation("Exited as the pipeline job run has been cleaned up as previous processing did not complete {JobIdentifier} for deposit {DepositId}.", request.JobIdentifier, request.DepositId);
                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors = [new Error { Message = "Cleaned up as previous processing did not complete" }],
                    CleanupProcessJob = true
                };
            }

            var deleteBrunnhildeResult = await DeleteBrunnhildeFoldersAndFiles(request, workspaceManager);
            if (deleteBrunnhildeResult.Failure)
            {
                logger.LogInformation("Brunnhilde deletion failed: " + deleteBrunnhildeResult.CodeAndMessage());
                // Do we just go ahead anyway?
            }

            // Now our workspaceManager is out of date, because METS has been modified.
            // So we can't use it again for further *content modifications*.
            // But we can use it for other properties, e.g. workspaceManager.IsBagItLayout won't have changed.

            var metadataPathForProcessFiles = workspaceManager.IsBagItLayout
                ? $"{processFolder}{separator}{request.DepositId}{separator}data{separator}metadata" //{separator}{BrunnhildeFolderName}
                : $"{processFolder}{separator}{request.DepositId}{separator}metadata";

            var metadataPathForProcessDirectories = workspaceManager.IsBagItLayout
                ? $"{processFolder}{separator}{request.DepositId}{separator}data{separator}metadata{separator}{BrunnhildeFolderName}" //{separator}{BrunnhildeFolderName}
                : $"{processFolder}{separator}{request.DepositId}{separator}metadata{separator}{BrunnhildeFolderName}"; //               

            logger.LogInformation("metadataPathForProcessFiles after brunnhilde process {metadataPathForProcessFiles}",
                metadataPathForProcessFiles);
            logger.LogInformation(
                "metadataPathForProcessDirectories after brunnhilde process {metadataPathForProcessDirectories}",
                metadataPathForProcessDirectories);
            logger.LogInformation("depositName after brunnhilde process {depositId}", request.DepositId);

            var (forceCompleteAfterDelete, cleanupProcessJobAfterDelete) = await CheckIfForceComplete(request, workspaceManager.Deposit, cancellationToken);
            // At this point we have not modified the METS file, the ETag for this workspace is still valid
            if (forceCompleteAfterDelete)
            {
                await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
                if (!cleanupProcessJobAfterDelete)
                {
                    logger.LogInformation("Exited as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                    return new ProcessPipelineResult
                    {
                        Status = PipelineJobStates.CompletedWithErrors,
                        Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} was force completed" }]
                    };
                }

                logger.LogInformation("Exited as the pipeline job run has been cleaned up as previous processing did not complete {JobIdentifier} for deposit {DepositId}.", request.JobIdentifier, request.DepositId);
                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors = [new Error { Message = "Cleaned up as previous processing did not complete" }],
                    CleanupProcessJob = true
                };
            }

            var (createFolderResultList, uploadFilesResultList, forceCompleteUpload, forceCompleteUploadCleanupProcess) = await UploadFilesToMetadataRecursively(
                request, metadataPathForProcessDirectories, metadataPathForProcessFiles, depositPath,
                workspaceManager.Deposit, cancellationToken);

            if (forceCompleteUpload || forceCompleteUploadCleanupProcess)
            {
                await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
                if (!forceCompleteUploadCleanupProcess)
                {
                    logger.LogInformation("Exited as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                    return new ProcessPipelineResult
                    {
                        Status = PipelineJobStates.CompletedWithErrors,
                        Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} was force completed" }]
                    };
                }

                logger.LogInformation("Exited as the pipeline job run has been cleaned up as previous processing did not complete {JobIdentifier} for deposit {DepositId}.", request.JobIdentifier, request.DepositId);
                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors = [new Error { Message = "Cleaned up as previous processing did not complete" }],
                    CleanupProcessJob = true
                };
            }

            foreach (var folderResult in createFolderResultList)
            {
                logger.LogInformation("{context} upload Success: {success}", folderResult?.Value?.Context,
                    folderResult?.Success);
            }

            foreach (var uploadFileResult in uploadFilesResultList)
            {
                logger.LogInformation("{context} upload Success: {success}", uploadFileResult?.Value?.Context,
                    uploadFileResult?.Success);
            }


            var (forceCompleteAfterUploads, cleanupProcessJobAfterUploads) = await CheckIfForceComplete(request, workspaceManager.Deposit, cancellationToken);
            // At this point we have not modified the METS file, the ETag for this workspace is still valid
            if (forceCompleteAfterUploads)
            {
                await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);
                if (!cleanupProcessJobAfterUploads)
                {
                    logger.LogInformation("Exited as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId} has been force completed.", request.JobIdentifier, request.DepositId);
                    return new ProcessPipelineResult
                    {
                        Status = PipelineJobStates.CompletedWithErrors,
                        Errors = [new Error { Message = $"Pipeline job run {request.JobIdentifier} for {request.DepositId} was force completed" }]
                    };
                }

                logger.LogInformation("Exited as the pipeline job run has been cleaned up as previous processing did not complete {JobIdentifier} for deposit {DepositId}.", request.JobIdentifier, request.DepositId);
                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors = [new Error { Message = "Cleaned up as previous processing did not complete" }],
                    CleanupProcessJob = true
                };
            }

            // This is a valid update to happen inside ExecuteBrunnhilde
            var pipelineJobsResult = await UpdateJobStatus(request, PipelineJobStates.MetadataCreated, CancellationToken.None);

            if (pipelineJobsResult.Value?.Errors is { Length: 0 })
                logger.LogInformation("Job {jobIdentifier} and deposit {depositId} pipeline run metadataCreated status logged",
                    request.JobIdentifier, request.DepositId);

            var metsResult = await AddObjectsToMets(request, depositPath);

            if(metsResult.Failure)
                logger.LogInformation("Issue adding objects to METS in pipeline run: {error}", metsResult.ErrorMessage);

            await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.Completed
            };

        }

        await TryReleaseLock(request, workspaceManager.Deposit, cancellationToken);

        return new ProcessPipelineResult
        {
            Status = PipelineJobStates.CompletedWithErrors,
            Errors = [new Error { Message = "Issue producing Brunnhilde files." }]
        };
    }

    private (string, string, string) GetFilePaths(WorkspaceManager workspaceManager)
    {
        var depositId = workspaceManager.DepositSlug;
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;
        string metadataPath;
        string metadataProcessPath;
        string objectPath;

        if (workspaceManager.IsBagItLayout)
        {
            metadataPath = $"{mountPath}{separator}{depositId}{separator}data{separator}metadata";
            metadataProcessPath =
                $"{processFolder}{separator}{depositId}{separator}data{separator}metadata{separator}{BrunnhildeFolderName}";
            objectPath = $"{mountPath}{separator}{depositId}{separator}data{separator}{objectFolder}";
        }
        else
        {
            metadataPath = $"{mountPath}{separator}{depositId}{separator}metadata";
            metadataProcessPath =
                $"{processFolder}{separator}{depositId}{separator}metadata{separator}{BrunnhildeFolderName}";
            objectPath = $"{mountPath}{separator}{depositId}{separator}{objectFolder}";
        }


        if (!Directory.Exists(processFolder) && processFolder != null)
            Directory.CreateDirectory(processFolder);

        if (!Directory.Exists(metadataProcessPath))
            Directory.CreateDirectory(metadataProcessPath);

        return (metadataPath, metadataProcessPath, objectPath);
    }

    private async Task<Result<ItemsAffected>> DeleteBrunnhildeFoldersAndFiles(
        ExecutePipelineJob request, WorkspaceManager workspaceManager)
    {
        // This is an expensive operation (refresh=true):
        var root = await workspaceManager.RefreshCombinedDirectory();

        var (directories, files) = root.Value!.Flatten();
        var deleteSelection = new DeleteSelection
        {
            DeleteFromDepositFiles = true,
            DeleteFromMets = true,
            Deposit = null,
            Items = []
        };
        var testPath = $"{FolderNames.Metadata}/{BrunnhildeFolderName}";
        foreach (var directory in directories)
        {
            if (directory.LocalPath!.StartsWith(testPath))
            {
                deleteSelection.Items.Add(new MinimalItem
                {
                    IsDirectory = true,
                    RelativePath = directory.LocalPath,
                    Whereabouts = Whereabouts.Both
                });
            }
        }

        foreach (var file in files)
        {
            if (file.LocalPath!.StartsWith(testPath))
            {
                deleteSelection.Items.Add(new MinimalItem
                {
                    IsDirectory = false,
                    RelativePath = file.LocalPath,
                    Whereabouts = Whereabouts.Both
                });
            }
        }

        var resultDelete = await workspaceManager.DeleteItems(deleteSelection, request.GetUserName());
        return resultDelete;
    }


    private async Task<(
        List<Result<CreateFolderResult>?> createSubFolderResult, 
        List<Result<SingleFileUploadResult>?>uploadFileResult, bool forceComplete, bool cleanupProcess)> UploadFilesToMetadataRecursively(
            ExecutePipelineJob request, 
            string sourcePathForDirectories, string sourcePathForFiles, string depositPath, 
            Deposit deposit, CancellationToken cancellationToken)
    {
        try
        {

            var (forceCompleteBeforeUpload, cleanupProcessBeforeUpload) = await CheckIfForceComplete(request, deposit, cancellationToken);
            // At this point we have not modified the METS file, the ETag for this workspace is still valid
            if (forceCompleteBeforeUpload || cleanupProcessBeforeUpload)
            {
                await TryReleaseLock(request, deposit, cancellationToken);
                logger.LogInformation("Exited UploadFilesToMetadataRecursively() method as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId}", request.JobIdentifier, request.DepositId);
                return (createSubFolderResult: [], uploadFileResult: [], forceCompleteBeforeUpload, cleanupProcessBeforeUpload);
            }

            var context = new StringBuilder();
            context.Append("metadata");

            logger.LogInformation($"context {context}");

            //create Brunnhilde folder first
            var createSubFolderResult = new List<Result<CreateFolderResult>?>
                {
                    await CreateMetadataSubFolderOnS3(request, sourcePathForDirectories)
                };

            //Now Create all of the directories
            foreach (var dirPath in Directory.GetDirectories(sourcePathForDirectories, "*", SearchOption.AllDirectories))
            {
                logger.LogInformation("dir path {dirPath}", dirPath);
                var (forceCompleteDirectoryUpload, cleanupProcessDirectoryUpload) = await CheckIfForceComplete(request, deposit, cancellationToken);
                // At this point we have not modified the METS file, the ETag for this workspace is still valid
                if (forceCompleteDirectoryUpload || cleanupProcessDirectoryUpload)
                {
                    await TryReleaseLock(request, deposit, cancellationToken);
                    logger.LogInformation("Exited UploadFilesToMetadataRecursively() method as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId}", request.JobIdentifier, request.DepositId);
                    return (createSubFolderResult: [], uploadFileResult: [], forceCompleteDirectoryUpload, cleanupProcessDirectoryUpload);
                }

                createSubFolderResult.Add(await CreateMetadataSubFolderOnS3(request, dirPath));
            }

            var uploadFileResult = new List<Result<SingleFileUploadResult>?>();

            foreach (var filePath in Directory.GetFiles(sourcePathForFiles, "*.*", SearchOption.AllDirectories))
            {
                logger.LogInformation("Upload file path {filePath}", filePath);
                if (filesToIgnore.Any(filePath.Contains))
                    continue;

                var (forceCompleteFileUpload, cleanupProcessFileUpload) = await CheckIfForceComplete(request, deposit, cancellationToken);
                // At this point we have not modified the METS file, the ETag for this workspace is still valid
                if (forceCompleteFileUpload || cleanupProcessFileUpload)
                {
                    await TryReleaseLock(request, deposit, cancellationToken);
                    logger.LogInformation("Exited UploadFilesToMetadataRecursively() method as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId}", request.JobIdentifier, request.DepositId);
                    return (createSubFolderResult: [], uploadFileResult: [], forceCompleteFileUpload, cleanupProcessFileUpload);
                }

                var (uploadFileToS3Result, uploadFileToS3ForcedComplete, uploadFileToS3CleanupProcess) = await UploadFileToDepositOnS3(request, filePath, sourcePathForFiles, deposit, cancellationToken);
                if (uploadFileToS3ForcedComplete || uploadFileToS3CleanupProcess)
                {
                    await TryReleaseLock(request, deposit, cancellationToken);
                    logger.LogInformation("Exited UploadFilesToMetadataRecursively() method as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId}", request.JobIdentifier, request.DepositId);
                    return (createSubFolderResult: [], uploadFileResult: [], uploadFileToS3ForcedComplete, uploadFileToS3CleanupProcess);
                }

                uploadFileResult.Add(uploadFileToS3Result);
            }

            foreach (var subFolder in createSubFolderResult)
            {
                logger.LogInformation(
                    $"subFolder.ErrorMessage {subFolder?.ErrorMessage} , subFolder?.Value?.Context {subFolder?.Value?.Context} subFolder?.Value?.Created {subFolder?.Value?.Created}");
            }

            foreach (var uploadFile in uploadFileResult)
            {
                logger.LogInformation(" uploadFile.Value.Context {context}", uploadFile?.Value?.Context);
            }

            if (createSubFolderResult.Any() && uploadFileResult.Any())
            {
                return (createSubFolderResult, uploadFileResult, false, false);
            }

        }
        catch (Exception ex)
        {
            logger.LogError(ex, " Caught error in copy files recursively from {sourcePathForFiles} to {depositPath}", sourcePathForFiles, depositPath);
            return (createSubFolderResult: [], uploadFileResult: [], false, false);
        }

        return (createSubFolderResult: [], uploadFileResult: [], false, false);
    }

    private async Task<Result<CreateFolderResult>?> CreateMetadataSubFolderOnS3(ExecutePipelineJob request, string dirPath)
    {
        // This is a content-changing operation
        var workspaceResult = await GetWorkspaceManager(request, true);
        var workspaceManager = workspaceResult.Value!;

        var context = new StringBuilder();
        var metadataContext = "metadata";
        context.Append(metadataContext);
        var di = new DirectoryInfo(dirPath);
        if (di.Parent?.Name.ToLower() == BrunnhildeFolderName &&
            !context.ToString().Contains($"/{BrunnhildeFolderName}")) //TODO
            context.Append($"/{BrunnhildeFolderName}");

        logger.LogInformation("BrunnhildeFolderName {BrunnhildeFolderName}", BrunnhildeFolderName);
        logger.LogInformation("di.Name {di.Name} context {context}", di.Name, context);
        var result = await workspaceManager.CreateFolder(
            di.Name, context.ToString(), false, request.GetUserName(), true);

        if (!result.Success)
        {
            logger.LogError("Error code for dir path {dirPath}: {errorCode}", dirPath, result.ErrorCode);
            logger.LogError("Error message for dir path {dirPath}: {errorMessage}", dirPath, result.ErrorMessage);
            logger.LogError("Error failure for dir path {dirPath}: {failure}", dirPath, result.Failure);
        }

        return result;
    }

    private async Task<(Result<SingleFileUploadResult>?, bool, bool)> UploadFileToDepositOnS3(ExecutePipelineJob request, string filePath,
        string sourcePath, Deposit deposit, CancellationToken cancellationToken)
    {
        var context = new StringBuilder();
        var metadataContext = "metadata";
        context.Append(metadataContext);

        var (forceCompleteUploadS3, cleanupProcessUploadS3) = await CheckIfForceComplete(request, deposit, cancellationToken);

        if (forceCompleteUploadS3 || cleanupProcessUploadS3)
        {
            logger.LogInformation("Exited UploadFileToDepositOnS3() method as the pipeline job run has been forced complete {JobIdentifier} for deposit {DepositId}", request.JobIdentifier, request.DepositId);
            await TryReleaseLock(request, deposit, cancellationToken);
            return (null, forceCompleteUploadS3, cleanupProcessUploadS3);
        }

        if (!filePath.Contains(BrunnhildeFolderName))
            return (null, false, false);

        var fi = new FileInfo(filePath);

        if (fi.Directory == null)
        {
            return (null, false, false);
        }

        var contextPath = metadataContext + "/" + Path.GetRelativePath(
            sourcePath,
            fi.Directory.FullName).Replace(@"\", "/");

        if (fi.Directory.Name.ToLower() == BrunnhildeFolderName &&
            !context.ToString().Contains($"/{BrunnhildeFolderName}"))
            context.Append($"/{BrunnhildeFolderName}");

        var checksum = Checksum.Sha256FromFile(fi);

        if (string.IsNullOrEmpty(checksum))
            return (null, false, false);

        var stream = GetFileStream(filePath);
        var result = await UploadFileToBucketDeposit(request, stream, filePath, contextPath, checksum);


        if (!result.Success)
        {
            logger.LogError("Error code for file path {filePath}: {errorCode}", filePath, result.ErrorCode);
            logger.LogError("Error message for file path {filePath}: {errorMessage}", filePath, result.ErrorMessage);
            logger.LogError("Error failure for file path {filePath}: {failure}", filePath, result.Failure);
        }
        else
        {
            logger.LogInformation("uploaded file {uploaded} with context {context}", result.Value?.Uploaded, result.Value?.Context);
        }

        await stream.DisposeAsync();
        return (result, false, false);
    }

    private async Task<Result<SingleFileUploadResult>> UploadFileToBucketDeposit(
        ExecutePipelineJob request, Stream stream, string filePath, string contextPath, string checksum)
    {
        // This is potentially expensive as it needs a NEW WorkspaceManager each time
        // TODO: We need a batch operation on WorkspaceManager to upload a set of small files.

        var fi = new FileInfo(filePath);
        try
        {
            MimeTypes.TryGetMimeType(filePath.GetSlug(), out var contentType);

            if (string.IsNullOrEmpty(contentType))
                return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError,
                    "Could not find file content type");

            var workspaceManagerResult = await GetWorkspaceManager(request, true);
            var result = await workspaceManagerResult.Value!.UploadSingleSmallFile(
                stream, stream.Length, fi.Name, checksum, fi.Name, contentType, contextPath, request.GetUserName(), true);

            return result;
        }
        catch (Exception ex)
        {
            return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError, ex.Message);
        }
    }

    private static Stream GetFileStream(string filePath)
    {
        if (!File.Exists(filePath)) throw new Exception($"{filePath} file not found.");
        Stream result = File.OpenRead(filePath);
        if (result.Length > 0)
        {
            result.Seek(0, SeekOrigin.Begin);
        }

        return result;
    }

    /// <summary>
    /// Force the objects/ folder contents to be re-processed - PATCH the METS
    /// </summary>
    /// <param name="request"></param>
    /// <param name="depositPath"></param>
    private async Task<Result<ItemsAffected>> AddObjectsToMets(ExecutePipelineJob request, string depositPath)
    {
        var workspaceManagerResult = await GetWorkspaceManager(request, true);
        var workspaceManager = workspaceManagerResult.Value!;
        var (_, _, objectPath) = GetFilePaths(workspaceManager);
        var minimalItems = new List<MinimalItem>();

        if (workspaceManager.IsBagItLayout)
            depositPath += "/data";

        foreach (var filePath in Directory.GetFiles(objectPath, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(
                depositPath,
                filePath).Replace(@"\", "/");

            minimalItems.Add(
                new MinimalItem
                {
                    RelativePath = relativePath,
                    IsDirectory = false,
                    Whereabouts = Whereabouts.Both
                });

        }

        var combinedResult = await workspaceManager.RefreshCombinedDirectory();
        if (combinedResult is not { Success: true, Value: not null })
        {
            logger.LogError("Could not read deposit file system.");
        }

        var wbsToAdd = new List<WorkingBase>();
        var contentRoot = combinedResult.Value;

        foreach (var item in minimalItems)
        {
            WorkingBase? wbToAdd = item.IsDirectory
                ? contentRoot?.FindDirectory(item.RelativePath)?.DirectoryInDeposit?.ToRootLayout()
                : contentRoot?.FindFile(item.RelativePath)?.FileInDeposit?.ToRootLayout();

            if (wbToAdd != null)
            {
                wbsToAdd.Add(wbToAdd);
            }
        }

        return await workspaceManager.AddItemsToMets(wbsToAdd, request.GetUserName());
    }

    private async Task CleanupPipelineRunsForDeposit(ExecutePipelineJob request)
    {
        var depositPipelineResults = 
            await preservationApiClient.GetPipelineJobResultsForDeposit(request.DepositId, CancellationToken.None);

        if (depositPipelineResults.Value == null)
            return;

        foreach (var jobResult in depositPipelineResults.Value)
        {
            if (jobResult.Status == PipelineJobStates.Running && jobResult.DateBegun <= DateTime.Now.Date)
            {

                if (string.IsNullOrEmpty(jobResult.JobId))
                    continue;
                
                var pipelineJobsResult = await UpdateAnyJobStatus(
                    request.DepositId, jobResult.JobId, 
                    PipelineJobStates.CompletedWithErrors, 
                    runUser: request.GetUserName(),
                    "Cleaned up old Pipeline Job for to allow for job " + request.JobIdentifier);

                if (pipelineJobsResult.Failure)
                    logger.LogError(
                        $"Could not record CompletedWithErrors status for old jobs for deposit {request.DepositId} job {request.JobIdentifier}");
            }
        }
    }


    private async Task<(bool, bool)> CheckIfForceComplete(ExecutePipelineJob request, Deposit deposit, CancellationToken cancellationToken)
    {
        var depositPipelineResults = await preservationApiClient.GetPipelineJobResultsForDeposit(
            request.DepositId, CancellationToken.None);

        if (depositPipelineResults.Value == null) return (false, false);
        var job = depositPipelineResults.Value.FirstOrDefault(
            x => x.JobId == request.JobIdentifier && x.Status == PipelineJobStates.CompletedWithErrors);
        var forceComplete = job != null;

        var cleanupProcessJob = job is { Errors: not null } &&
                                job.Errors.Any(x => x.Message.Contains("Cleaned up as previous processing did not complete"));
        
        if (forceComplete)
        {
            await TryReleaseLock(request, deposit, cancellationToken);
        }

        return (forceComplete, cleanupProcessJob);
    }

    private async Task<Result> TryReleaseLock(ExecutePipelineJob request, Deposit deposit, CancellationToken cancellationToken)
    {
        var releaseLockResult =
            await preservationApiClient.ReleaseDepositLock(deposit, cancellationToken);
        logger.LogInformation($"releaseLockResult: {releaseLockResult.Success}");
        if (releaseLockResult is { Failure: true })
        {
            logger.LogError($"Could not release lock for Job {request.JobIdentifier}");
        }

        return releaseLockResult;
    }

    private async void CheckIfProcessRunning(object? source, ElapsedEventArgs eventArgs, ExecutePipelineJob request, Deposit deposit, CancellationToken cancellationToken)
    {
        try
        {
            var (forceComplete, _) = await CheckIfForceComplete(request, deposit, cancellationToken);
            if (forceComplete)
            {
                _streamReader = null;
                logger.LogInformation("This job {jobId} was force completed and thus attempting kill the Brunnhilde process", request.JobIdentifier);

                try
                {
                    var process = Process.GetProcessById(_processId);
                    Process[] pname = Process.GetProcessesByName(process.ProcessName);

                    if (pname.Length > 0 && !process.HasExited)
                        process.Kill();

                    logger.LogInformation("Process killed for job id {jobId}", request.JobIdentifier);
                    ProcessTimer.Stop();
                    ProcessTimer.Enabled = false;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Attempted to kill process for job {jobId}", request.JobIdentifier);
                    ProcessTimer.Stop();
                    ProcessTimer.Enabled = false;
                }


                ProcessTimer.Stop();
                ProcessTimer.Enabled = false;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "CheckIfProcessRunning has thrown an exception.");
            //await m_TokensCatalog[BrunnhildeProcessId].CancelAsync();
            //await m_TokensCatalog[MonitorForceCompleteId].CancelAsync();
        }
    }
}


