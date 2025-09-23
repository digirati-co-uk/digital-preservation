using Azure.Core;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Preservation.Client;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Checksum = DigitalPreservation.Utils.Checksum;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ExecutePipelineJob(string jobIdentifier, string depositId, string? runUser) : IRequest<Result>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string DepositId { get; } = depositId;
    public string? RunUser { get; } = runUser;
}


public class ProcessPipelineJobHandler(
    ILogger<ProcessPipelineJobHandler> logger,
    IMediator mediator,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions,
    WorkspaceManagerFactory workspaceManagerFactory,
    IPreservationApiClient preservationApiClient) : IRequestHandler<ExecutePipelineJob, Result>

{
    private const string BrunnhildeFolderName = "brunnhilde";
    private readonly string[] filesToIgnore = ["tree.txt"];
    private string? jobIdentifier;
    private string? runUser;


    /// <summary>
    /// Reacquiring a new WorkspaceManager is not expensive, but refreshing the file system is
    /// (e.g., GetCombinedDirectory(true))
    /// </summary>
    /// <param name="depositId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<Result<WorkspaceManager>> GetWorkspaceManager(string depositId, bool refresh,
        CancellationToken cancellationToken = default)
    {
        var response = await preservationApiClient.GetDeposit(depositId, cancellationToken);
        if (response.Failure || response.Value == null)
        {
            return Result.FailNotNull<WorkspaceManager>(response.ErrorCode ?? ErrorCodes.UnknownError,
                $"Could not process pipeline job for job id {jobIdentifier} and deposit {depositId} as could not find the deposit.");
        }

        var deposit = response.Value;
        var workspaceManager = await workspaceManagerFactory.CreateAsync(deposit, refresh);
        foreach (var warning in workspaceManager.Warnings)
        {
            logger.LogWarning(warning);
        }

        return Result.OkNotNull(workspaceManager);
    }


    public async Task<Result> Handle(ExecutePipelineJob request, CancellationToken cancellationToken)
    {
        jobIdentifier = request.JobIdentifier;
        runUser = request.RunUser ?? "PipelineApi";
        //var forceCompleted = await CheckIfForceComplete(request.DepositId, jobIdentifier);

        await CleanupPipelineRunsForDeposit(request.DepositId);

        var workspaceResult = await GetWorkspaceManager(request.DepositId, true, cancellationToken);
        if (workspaceResult.Failure || workspaceResult.Value?.Deposit == null)
        {
            return Result.Fail(workspaceResult.ErrorCode ?? ErrorCodes.UnknownError,
                $"Could not process pipeline job for job id {request.JobIdentifier} and deposit {request.DepositId} as could not find the deposit.");
        }

        try
        {
            if (await CheckIfForceComplete(request.DepositId, jobIdentifier))
            {
                var releaseLockResult =
                    await preservationApiClient.ReleaseDepositLock(workspaceResult.Value.Deposit,
                        CancellationToken.None);
                logger.LogInformation($"releaseLockResult: {releaseLockResult.Success}");
                if (releaseLockResult is { Failure: true })
                {
                    logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                }

                return Result.FailNotNull<Result>(ErrorCodes.UnknownError,
                    $"Pipeline job run {jobIdentifier} for deposit {request.DepositId} has been force completed.");
            }


            await mediator.Send(new LogPipelineJobStatus(
                request.DepositId, request.JobIdentifier, PipelineJobStates.Running, runUser), cancellationToken);

            var result = await ExecuteBrunnhilde(workspaceResult.Value);

            logger.LogInformation("Execute Brunnhilde result test {status} {errors} ", result?.Status, result?.Errors);
            return result?.Status == PipelineJobStates.Completed
                ? Result.Ok()
                : Result.FailNotNull<Result>(ErrorCodes.UnknownError,
                    $"Could not complete pipeline run {result?.Errors?.FirstOrDefault()?.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Caught error in PipelineJob handler for job id {jobIdentifier} and deposit {depositId}",
                request.JobIdentifier, request.DepositId);

            var releaseLockResult =
                await preservationApiClient.ReleaseDepositLock(workspaceResult.Value.Deposit, CancellationToken.None);
            logger.LogInformation("releaseLockResult: {success}", releaseLockResult.Success);

            if (releaseLockResult is { Failure: true })
            {
                logger.LogError("Could not release lock for Job {jobIdentifier} Completed status logged",
                    jobIdentifier);
            }

            var pipelineJobsResult = await mediator.Send(
                new LogPipelineJobStatus(request.DepositId, request.JobIdentifier,
                    PipelineJobStates.CompletedWithErrors, runUser, ex.Message), cancellationToken);

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

    private async Task<ProcessPipelineResult?> ExecuteBrunnhilde(WorkspaceManager workspaceManager)
    {
        var depositId = workspaceManager.DepositSlug!;
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;

        if (!Directory.Exists(mountPath))
        {
            logger.LogError("S3 mount path could not be found at {mountPath}", mountPath);

            var releaseLockResult1 =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation("releaseLockResult: {success}", releaseLockResult1.Success);

            if (releaseLockResult1 is { Failure: true })
            {
                logger.LogError("Could not release lock for Job {jobIdentifier} Completed status logged",
                    jobIdentifier);
            }

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors = [new Error { Message = $"S3 mount path could not be found at {mountPath}" }],
                ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
            };
        }

        var (metadataPath, metadataProcessPath, objectPath) = GetFilePaths(workspaceManager);

        if (!Directory.Exists(objectPath))
        {
            logger.LogError("Deposit {depositId} folder and contents could not be found at {objectPath}", depositId,
                objectPath);

            var releaseLockResult1 =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation("releaseLockResult: {success}", releaseLockResult1.Success);

            if (releaseLockResult1 is { Failure: true })
            {
                logger.LogError("Could not release lock for Job {jobIdentifier} Completed status logged",
                    jobIdentifier);
            }

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors = [new Error { Message = $" Could not retrieve deposit for {depositId} and could not unlock" }],
                ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
            };
        }

        logger.LogInformation("Metadata folder value: {metadataPath}", metadataPath);
        logger.LogInformation("Metadata process folder value: {metadataProcessPath}", metadataProcessPath);
        logger.LogInformation("Object folder path value: {objectPath}", objectPath);

        if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
        {
            var releaseLockResult1 =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
            if (releaseLockResult1 is { Failure: true })
            {
                logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
            }

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors =
                [
                    new Error { Message = $"Pipeline job run {jobIdentifier} for {depositId} was force completed" }
                ],
                ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
            };
        }

        var start = new ProcessStartInfo
        {
            FileName = brunnhildeOptions.Value.PathToPython,
            Arguments =
                $"  {brunnhildeOptions.Value.PathToBrunnhilde} --hash sha256 {objectPath} {metadataProcessPath}  --overwrite ",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(start);
        using var reader = process?.StandardOutput;

        if (reader == null)
        {
            logger.LogError("Issue executing Brunnhilde process: process?.StandardOutput is null");

            logger.LogError("Caught error in PipelineJob handler for job id {jobIdentifier} and deposit {depositId}",
                jobIdentifier, depositId);

            var releaseLockResult1 =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation("releaseLockResult: {success}", releaseLockResult1.Success);
            if (releaseLockResult1 is { Failure: true })
            {
                logger.LogError("Could not release lock for Job {jobIdentifier} Completed status logged",
                    jobIdentifier);
            }

            await mediator.Send(new LogPipelineJobStatus(
                depositId, jobIdentifier!, PipelineJobStates.CompletedWithErrors,
                runUser!, "Issue executing Brunnhilde process as the reader is null"));

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                Errors =
                [
                    new Error
                    {
                        Message =
                            $" Issue executing Brunnhilde process as the reader is null for {depositId} and job id {jobIdentifier}"
                    }
                ],
                ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty
            };
        }

        var result = await reader.ReadToEndAsync();
        var brunnhildeExecutionSuccess = result.Contains("Brunnhilde characterization complete.");
        logger.LogInformation("Brunnhilde result success: {brunnhildeExecutionSuccess}", brunnhildeExecutionSuccess);

        if (brunnhildeExecutionSuccess)
        {
            logger.LogInformation("Brunnhilde creation successful");
            var depositPath = $"{mountPath}{separator}{depositId}";

            // At this point we have not modified the METS file, the ETag for this workspace is still valid
            if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
            {
                var releaseLockResult1 =
                    await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
                logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
                if (releaseLockResult1 is { Failure: true })
                {
                    logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                }

                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors =
                    [
                        new Error { Message = $"Pipeline job run {jobIdentifier} for {depositId} was force completed" }
                    ],
                    ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
                };
            }

            var deleteBrunnhildeResult = await DeleteBrunnhildeFoldersAndFiles(workspaceManager);
            if (deleteBrunnhildeResult.Failure)
            {
                logger.LogInformation("Brunnhilde deletion failed: " + deleteBrunnhildeResult.CodeAndMessage());
                // Do we just go ahead anyway?
            }

            // Now our workspaceManager is out of date, because METS has been modified.
            // So we can't use it again for further *content modifications*.
            // But we can use it for other properties, e.g. workspaceManager.IsBagItLayout won't have changed.

            var metadataPathForProcessFiles = workspaceManager.IsBagItLayout
                ? $"{processFolder}{separator}{depositId}{separator}data{separator}metadata" //{separator}{BrunnhildeFolderName}
                : $"{processFolder}{separator}{depositId}{separator}metadata";

            var metadataPathForProcessDirectories = workspaceManager.IsBagItLayout
                ? $"{processFolder}{separator}{depositId}{separator}data{separator}metadata{separator}{BrunnhildeFolderName}" //{separator}{BrunnhildeFolderName}
                : $"{processFolder}{separator}{depositId}{separator}metadata{separator}{BrunnhildeFolderName}"; //               

            logger.LogInformation("metadataPathForProcessFiles after brunnhilde process {metadataPathForProcessFiles}",
                metadataPathForProcessFiles);
            logger.LogInformation(
                "metadataPathForProcessDirectories after brunnhilde process {metadataPathForProcessDirectories}",
                metadataPathForProcessDirectories);
            logger.LogInformation("depositName after brunnhilde process {depositId}", depositId);

            if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
            {
                var releaseLockResult1 =
                    await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
                logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
                if (releaseLockResult1 is { Failure: true })
                {
                    logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                }

                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors =
                    [
                        new Error { Message = $"Pipeline job run {jobIdentifier} for {depositId} was force completed" }
                    ],
                    ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
                };

            }

            var (createFolderResultList, uploadFilesResultList) = await UploadFilesToMetadataRecursively(
                depositId, metadataPathForProcessDirectories, metadataPathForProcessFiles, depositPath,
                workspaceManager);

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

            if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
            {
                var releaseLockResult1 =
                    await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
                logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");

                if (releaseLockResult1 is { Failure: true })
                {
                    logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                }

                return new ProcessPipelineResult
                {
                    Status = PipelineJobStates.CompletedWithErrors,
                    Errors =
                    [
                        new Error { Message = $"Pipeline job run {jobIdentifier} for {depositId} was force completed" }
                    ],
                    ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
                };
            }

            var releaseLockResult =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation("releaseLockResult: {success}", releaseLockResult.Success);

            if (releaseLockResult is { Failure: true })
            {
                logger.LogError("Could not release lock for Job {jobIdentifier} Completed status logged", jobIdentifier);
            }

            //check for force complete
            var pipelineJobsResult = await mediator.Send(
                new LogPipelineJobStatus(depositId, jobIdentifier!, PipelineJobStates.Completed, runUser!));

            if (pipelineJobsResult.Value?.Errors is { Length: 0 })
                logger.LogInformation("Job {jobIdentifier} and deposit {depositId} pipeline run Completed status logged",
                    jobIdentifier, depositId);


            await AddObjectsToMets(depositId, depositPath);

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.Completed,
                ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
            };


        }
        else
        {
            var releaseLockResult =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation("releaseLockResult: {success}", releaseLockResult.Success);

            if (releaseLockResult is { Failure: true })
            {
                logger.LogError("Could not release lock for Job {jobIdentifier} Completed status logged", jobIdentifier);
            }

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositId, jobIdentifier!,
                PipelineJobStates.CompletedWithErrors,
                runUser!, "Issue producing Brunnhilde files."));

            if (pipelineJobsResult.Failure)
                logger.LogError("Could not record CompletedWithErrors status for deposit {depositId} job {jobIdentifier}",
                    depositId, jobIdentifier);

            return new ProcessPipelineResult
            {
                Status = PipelineJobStates.CompletedWithErrors,
                ArchivalGroup = workspaceManager.Deposit.ArchivalGroupName ?? string.Empty,
                Errors = [new Error { Message = "Issue producing Brunnhilde files." }],
            };
        }

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

    private async Task<Result<ItemsAffected>> DeleteBrunnhildeFoldersAndFiles(WorkspaceManager workspaceManager)
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

        var resultDelete = await workspaceManager.DeleteItems(deleteSelection, runUser!);
        return resultDelete;
    }


    private async Task<(List<Result<CreateFolderResult>?> createSubFolderResult, List<Result<SingleFileUploadResult>?>
            uploadFileResult)> UploadFilesToMetadataRecursively(
            string depositId, string sourcePathForDirectories, string sourcePathForFiles, string depositPath, WorkspaceManager workspaceManager)
    {
        try
        {
            if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
            {
                var releaseLockResult1 =
                    await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
                logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
                if (releaseLockResult1 is { Failure: true })
                {
                    logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                }

                return (createSubFolderResult: [], uploadFileResult: []);
            }

            var context = new StringBuilder();
            context.Append("metadata");

            logger.LogInformation($"context {context}");

            //create Brunnhilde folder first
            var createSubFolderResult = new List<Result<CreateFolderResult>?>
                {
                    await CreateMetadataSubFolderOnS3(depositId, sourcePathForDirectories)
                };

            //Now Create all of the directories
            foreach (var dirPath in
                     Directory.GetDirectories(sourcePathForDirectories, "*", SearchOption.AllDirectories))
            {
                logger.LogInformation("dir path {dirPath}", dirPath);

                if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
                {
                    var releaseLockResult1 =
                        await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit,
                            CancellationToken.None);
                    logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
                    if (releaseLockResult1 is { Failure: true })
                    {
                        logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                    }

                    return (createSubFolderResult: [], uploadFileResult: []);
                }

                createSubFolderResult.Add(await CreateMetadataSubFolderOnS3(depositId, dirPath));
            }

            var uploadFileResult = new List<Result<SingleFileUploadResult>?>();

            foreach (var filePath in Directory.GetFiles(sourcePathForFiles, "*.*", SearchOption.AllDirectories))
            {
                logger.LogInformation("Upload file path {filePath}", filePath);

                if (filesToIgnore.Any(filePath.Contains))
                    continue;

                if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
                {
                    var releaseLockResult1 =
                        await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
                    logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
                    if (releaseLockResult1 is { Failure: true })
                    {
                        logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
                    }

                    return (createSubFolderResult: [], uploadFileResult: []);
                }

                uploadFileResult.Add(await UploadFileToDepositOnS3(depositId, filePath, sourcePathForFiles, workspaceManager));
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
                return (createSubFolderResult, uploadFileResult);
            }

        }
        catch (Exception ex)
        {
            logger.LogError(ex, " Caught error in copy files recursively from {sourcePathForFiles} to {depositPath}", sourcePathForFiles, depositPath);
        }

        return (createSubFolderResult: [], uploadFileResult: []);
    }

    private async Task<Result<CreateFolderResult>?> CreateMetadataSubFolderOnS3(string depositId, string dirPath)
    {
        // This is a content-changing operation
        var workspaceResult = await GetWorkspaceManager(depositId, true);
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
        var result = await workspaceManager.CreateFolder(di.Name, context.ToString(), false, runUser, true);

        if (!result.Success)
        {
            logger.LogError("Error code for dir path {dirPath}: {errorCode}", dirPath, result.ErrorCode);
            logger.LogError("Error message for dir path {dirPath}: {errorMessage}", dirPath, result.ErrorMessage);
            logger.LogError("Error failure for dir path {dirPath}: {failure}", dirPath, result.Failure);
        }

        return result;
    }

    private async Task<Result<SingleFileUploadResult>?> UploadFileToDepositOnS3(string depositId, string filePath,
        string sourcePath, WorkspaceManager workspaceManager)
    {
        var context = new StringBuilder();
        var metadataContext = "metadata";
        context.Append(metadataContext);

        if (!string.IsNullOrEmpty(jobIdentifier) && await CheckIfForceComplete(depositId, jobIdentifier))
        {
            var releaseLockResult1 =
                await preservationApiClient.ReleaseDepositLock(workspaceManager.Deposit, CancellationToken.None);
            logger.LogInformation($"releaseLockResult: {releaseLockResult1.Success}");
            if (releaseLockResult1 is { Failure: true })
            {
                logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
            }

            return null;
        }

        if (!filePath.Contains(BrunnhildeFolderName))
            return null;

        var fi = new FileInfo(filePath);

        if (fi.Directory == null)
        {
            return null;
        }

        var contextPath = metadataContext + "/" + Path.GetRelativePath(
            sourcePath,
            fi.Directory.FullName).Replace(@"\", "/");

        if (fi.Directory.Name.ToLower() == BrunnhildeFolderName &&
            !context.ToString().Contains($"/{BrunnhildeFolderName}"))
            context.Append($"/{BrunnhildeFolderName}");

        var checksum = Checksum.Sha256FromFile(fi);

        if (string.IsNullOrEmpty(checksum))
            return null;

        var stream = GetFileStream(filePath);
        var result = await UploadFileToBucketDeposit(depositId, stream, filePath, contextPath, checksum);


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
        return result;
    }

    private async Task<Result<SingleFileUploadResult>> UploadFileToBucketDeposit(
        string depositId, Stream stream, string filePath, string contextPath, string checksum)
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

            var workspaceManagerResult = await GetWorkspaceManager(depositId, true);
            var result = await workspaceManagerResult.Value!.UploadSingleSmallFile(
                stream, stream.Length, fi.Name, checksum, fi.Name, contentType, contextPath, runUser!, true);

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
    /// <param name="depositId"></param>
    /// <param name="depositPath"></param>
    private async Task AddObjectsToMets(string depositId, string depositPath)
    {
        var workspaceManagerResult = await GetWorkspaceManager(depositId, true);
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

        await workspaceManager.AddItemsToMets(wbsToAdd, runUser!);
        await workspaceManager.RefreshCombinedDirectory();
    }

    private async Task CleanupPipelineRunsForDeposit(string depositId)
    {
        var depositPipelineResults = await preservationApiClient.GetPipelineJobResultsForDeposit(depositId, new CancellationToken());

        if (depositPipelineResults.Value == null)
            return;

        foreach (var jobResult in depositPipelineResults.Value)
        {
            if (jobResult.Status == PipelineJobStates.Running && jobResult.DateBegun <= DateTime.Now.Date)
            {

                if (string.IsNullOrEmpty(jobResult.JobId))
                    continue;
                var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositId, jobResult.JobId,
                    PipelineJobStates.CompletedWithErrors,
                    runUser!, "Issue producing Brunnhilde files."));

                if (pipelineJobsResult.Failure)
                    logger.LogError(
                        $"Could not record CompletedWithErrors status for deposit {depositId} job {jobIdentifier}");
            }
        }
    }

    private async Task<bool> CheckIfForceComplete(string depositId, string jobId)
    {
        var depositPipelineResults = await preservationApiClient.GetPipelineJobResultsForDeposit(depositId, new CancellationToken());

        if (depositPipelineResults.Value == null) return false;
        var job = depositPipelineResults.Value.FirstOrDefault(x => x.JobId == jobId && x.Status == PipelineJobStates.CompletedWithErrors);
        return job != null;
    }

}


