using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Preservation.Client;
using System.Diagnostics;
using System.Text;
using Checksum = DigitalPreservation.Utils.Checksum;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ExecutePipelineJob(string jobIdentifier, string depositName, string? runUser) : IRequest<Result>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string DepositName { get; } = depositName;
    public string? RunUser { get; set; } = runUser;
}


public class ProcessPipelineJobHandler(
    ILogger<ProcessPipelineJobHandler> logger,
    IMediator mediator,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions,
    WorkspaceManagerFactory workspaceManagerFactory,
    IPreservationApiClient preservationApiClient) : IRequestHandler<ExecutePipelineJob, Result>

{
    //private WorkspaceManager WorkspaceManager { get; set; } = workspaceManager;
    private const string BrunnhildeFolderName = "brunnhilde";
    private readonly string[] filesToIgnore = ["tree.txt"];
    private WorkspaceManager? workspaceManagerWorker;

    public async Task<Result> Handle(ExecutePipelineJob request, CancellationToken cancellationToken)
    {
        var jobId = request.JobIdentifier;
        var depositId = request.DepositName;
        var runUser = request.RunUser;

        try
        {
            var response = await preservationApiClient.GetDeposit(depositId, cancellationToken);
            var deposit = response.Value;

            if(deposit == null)
                return Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not publish pipeline job for job id {jobId} and deposit {depositId} as could not find the deposit ");

            workspaceManagerWorker = workspaceManagerFactory.Create(deposit);

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositId, jobId, PipelineJobStates.Running,
                runUser ?? "PipelineApi"), cancellationToken);

            var result = await ExecuteBrunnhilde(jobId, request.DepositName, runUser);
            return result?.Status == PipelineJobStates.Completed ? Result.Ok() : Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not complete pipeline run {result?.Errors?.FirstOrDefault()?.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $" Caught error in PipelineJob handler for job id {jobId} and deposit {depositId} {ex.Message}");

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositId, jobId, PipelineJobStates.CompletedWithErrors,
                runUser ?? "PipelineApi", ex.Message), cancellationToken);

            if (pipelineJobsResult?.Value?.Errors is { Length: 0 })
                logger.LogInformation($"Job {jobId} Running status CompletedWithErrors logged");

            return Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not publish pipeline job for job id {jobId} and deposit {depositId}: " + ex.Message);
        }
        finally
        {
            CleanupProcessFolder(depositId);
        }

    }

    private void CleanupProcessFolder(string depositName)
    {
        var processFolder = brunnhildeOptions.Value.ProcessFolder;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var metadataPathForProcessDelete = $"{processFolder}{separator}{depositName}";
        Directory.Delete(metadataPathForProcessDelete, true);
    }

    private async Task<ProcessPipelineResult?> ExecuteBrunnhilde(string jobIdentifier, string depositName, string? runUser)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;

        var (metadataPath, metadataProcessPath, objectPath) = await GetFilePaths(depositName);

        if (!Directory.Exists(objectPath))
        {
            logger.LogError($"Deposit {depositName} folder and contents could not be found at {objectPath}");
            logger.LogInformation($"Metadata folder");

            var response = await preservationApiClient.GetDeposit(depositName);
            var deposit = response.Value;

            return new ProcessPipelineResult
            {
                Status = "CompletedWithErrors",
                Errors = [new Error { Message = $" Could not retrieve deposit for {depositName} and could not unlock" }],
                ArchivalGroup = deposit?.ArchivalGroupName ?? string.Empty,
            };
        }

        logger.LogInformation($"Metadata folder value: {metadataPath}");
        logger.LogInformation($"Metadata process folder value: {metadataProcessPath}");
        logger.LogInformation($"Object folder path value: {objectPath}");

        var start = new ProcessStartInfo
        {
            FileName = brunnhildeOptions.Value.PathToPython,
            Arguments = $"  {brunnhildeOptions.Value.PathToBrunnhilde} --hash sha256 {objectPath} {metadataProcessPath}  --overwrite ",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(start);
        using var reader = process?.StandardOutput;

        if (reader == null)
        {
            var response = await preservationApiClient.GetDeposit(depositName);
            var deposit = response.Value;

            logger.LogError($" Issue executing Brunnhilde process as the reader is null");

            await mediator.Send(new LogPipelineJobStatus(depositName, jobIdentifier, PipelineJobStates.CompletedWithErrors,
                runUser ?? "PipelineApi", $" Issue executing Brunnhilde process as the reader is null"));

            return new ProcessPipelineResult
            {
                Status = "CompletedWithErrors",
                Errors = [new Error { Message = $" Issue executing Brunnhilde process as the reader is null for {depositName} and job id {jobIdentifier}" }],
                ArchivalGroup = deposit?.ArchivalGroupName ?? string.Empty,
            };
        }

        var result = await reader.ReadToEndAsync();

        var brunnhildeExecutionSuccess = result?.Contains("Brunnhilde characterization complete.");

        logger.LogInformation($"Brunnhilde result success {brunnhildeExecutionSuccess}");

        if (!string.IsNullOrEmpty(result) && result.Contains("Brunnhilde characterization complete."))
        {
            logger.LogInformation($"Brunnhilde creation successful");
            await workspaceManagerWorker.GetCombinedDirectory(true);

            var depositPath = $"{mountPath}{separator}{depositName}";
            await DeleteBrunnhildeFoldersAndFiles(depositName, metadataPath, depositPath, runUser);

            var metadataPathForProcessFiles = workspaceManagerWorker.IsBagItLayout ? $"{processFolder}{separator}{depositName}{separator}data{separator}metadata"  //{separator}{BrunnhildeFolderName}
                : $"{processFolder}{separator}{depositName}{separator}metadata";
                                                                                 
            var metadataPathForProcessDirectories = workspaceManagerWorker.IsBagItLayout ? $"{processFolder}{separator}{depositName}{separator}data{separator}metadata{separator}{BrunnhildeFolderName}"  //{separator}{BrunnhildeFolderName}
                : $"{processFolder}{separator}{depositName}{separator}metadata{separator}{BrunnhildeFolderName}"; //               

            logger.LogInformation($"metadataPathForProcessFiles after brunnhilde process {metadataPathForProcessFiles}");
            logger.LogInformation($"metadataPathForProcessDirectories after brunnhilde process {metadataPathForProcessDirectories}");
            logger.LogInformation($"depositName after brunnhilde process {depositName}");

            var (createFolderResultList, uploadFilesResultList) = await UploadFilesToMetadataRecursively(depositName, metadataPathForProcessDirectories, metadataPathForProcessFiles, depositPath, runUser ?? "PipelineApi");

            foreach (var folderResult in createFolderResultList)
            {
                logger.LogInformation($"{folderResult?.Value?.Context} upload Success: {folderResult?.Success}");
            }

            foreach (var uploadFileResult in uploadFilesResultList)
            {
                logger.LogInformation($"{uploadFileResult?.Value?.Context} upload Success: {uploadFileResult?.Success}");
            }
            var response = await preservationApiClient.GetDeposit(depositName);
            var deposit = response.Value;

            if (deposit == null)
            {
                logger.LogError($" Could not retrieve deposit for {depositName} and could not unlock");

                await mediator.Send(new LogPipelineJobStatus(depositName, jobIdentifier, PipelineJobStates.CompletedWithErrors,
                    runUser ?? "PipelineApi", $" Could not retrieve deposit for {depositName} and could not unlock"));

                return new ProcessPipelineResult
                {
                    Status = "CompletedWithErrors",
                    Errors = [ new Error { Message = $" Could not retrieve deposit for {depositName} and could not unlock" }],
                    ArchivalGroup = deposit?.ArchivalGroupName ?? string.Empty,
                };
            }

            var releaseLockResult = await preservationApiClient.ReleaseDepositLock(deposit, new CancellationToken());
            logger.LogInformation($"releaseLockResult: {releaseLockResult.Success}");
            if (releaseLockResult is { Failure: true })
            {
                logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
            }

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositName, jobIdentifier, PipelineJobStates.Completed,
                runUser ?? "PipelineApi"));

            if (pipelineJobsResult?.Value?.Errors is { Length: 0 })
                logger.LogInformation($"Job {jobIdentifier} and deposit {depositName} pipeline run Completed status logged");

            await AddObjectsToMETS(depositName, depositPath, runUser ?? "PipelineApi");

            return new ProcessPipelineResult
            {
                Status = "Completed",
                ArchivalGroup = deposit?.ArchivalGroupName ?? string.Empty,
            };
        }
        else
        {
            var response = await preservationApiClient.GetDeposit(depositName);
            var deposit = response.Value;

            if (deposit == null)
            {
                logger.LogError($" Could not retrieve deposit for {depositName} and could not unlock");

               var logJobsResult = await mediator.Send(new LogPipelineJobStatus(depositName, jobIdentifier, PipelineJobStates.CompletedWithErrors,
                    runUser ?? "PipelineApi", $" Could not retrieve deposit for {depositName} and could not unlock"));

               if (logJobsResult.Failure)
                   logger.LogError($"Could not record CompletedWithErrors status for deposit {depositName} job {jobIdentifier}");

                return new ProcessPipelineResult
                {
                    Status = "CompletedWithErrors",
                    Errors = [new Error { Message = $" Could not retrieve deposit for {depositName} and could not unlock" }],
                    ArchivalGroup = deposit?.ArchivalGroupName ?? string.Empty,
                };
            }

            var releaseLockResult = await preservationApiClient.ReleaseDepositLock(deposit, new CancellationToken());
            logger.LogInformation($"releaseLockResult: {releaseLockResult.Success}");
            if (releaseLockResult is { Failure: true })
            {
                logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
            }

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositName, jobIdentifier, PipelineJobStates.CompletedWithErrors,
                runUser ?? "PipelineApi", "Issue producing Brunnhilde files."));

            if(pipelineJobsResult.Failure)
                logger.LogError($"Could not record CompletedWithErrors status for deposit {depositName} job {jobIdentifier}");

            return new ProcessPipelineResult
            {
                Status = "CompletedWithErrors",
                ArchivalGroup = deposit?.ArchivalGroupName ?? string.Empty,
                Errors = [new Error { Message = "Issue producing Brunnhilde files." }],
            };
        }
    }

    private async Task<(string, string, string)> GetFilePaths(string depositName)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;
        string metadataPath;
        string metadataProcessPath;
        string objectPath;

        await workspaceManagerWorker.GetCombinedDirectory(true);

        if (workspaceManagerWorker.IsBagItLayout)
        {
            metadataPath = $"{mountPath}{separator}{depositName}{separator}data{separator}metadata";
            metadataProcessPath = $"{processFolder}{separator}{depositName}{separator}data{separator}metadata{separator}{BrunnhildeFolderName}";
            objectPath = $"{mountPath}{separator}{depositName}{separator}data{separator}{objectFolder}";
        }
        else
        {
            metadataPath = $"{mountPath}{separator}{depositName}{separator}metadata";
            metadataProcessPath = $"{processFolder}{separator}{depositName}{separator}metadata{separator}{BrunnhildeFolderName}";
            objectPath = $"{mountPath}{separator}{depositName}{separator}{objectFolder}";
        }


        if (!Directory.Exists(processFolder))
            Directory.CreateDirectory(processFolder);

        if (!Directory.Exists(metadataProcessPath))
            Directory.CreateDirectory(metadataProcessPath);

        return (metadataPath, metadataProcessPath, objectPath);
    }

    private async Task DeleteBrunnhildeFoldersAndFiles(string depositId, string metadataPath, string depositPath, string? runUser)
    {
        //get deposit to create workspace manager
        var response = await preservationApiClient.GetDeposit(depositId);
        var deposit = response.Value;

        if (deposit == null)
        {
            logger.LogError($" Could not retrieve deposit for {depositId}");
            return;
        }

        await workspaceManagerWorker.GetCombinedDirectory(true);

        if (workspaceManagerWorker.IsBagItLayout)
            depositPath += "/data";

        foreach (var filePath in Directory.GetFiles(metadataPath, "*.*", SearchOption.AllDirectories))
        {
            logger.LogInformation($" filepath {filePath} in DeleteBrunnhildeFoldersAndFiles");
            if (!filePath.Contains(BrunnhildeFolderName))
                continue;

            var relativePath = Path.GetRelativePath(
                depositPath,
                filePath).Replace(@"\", "/").ToLower();

            logger.LogInformation($"relative path in get files {relativePath} in DeleteBrunnhildeFoldersAndFiles");

            await DeleteFolderOrFile(depositId, relativePath, false, runUser);
        }

        var metadataContext = "metadata";

        foreach (var dirPath in Directory.GetDirectories(metadataPath, "*", SearchOption.AllDirectories))
        {
            if (!dirPath.Contains(BrunnhildeFolderName))
                continue;

            var relativePath = Path.GetRelativePath(
                depositPath,
                dirPath).Replace(@"\", "/").ToLower();

            logger.LogInformation($"relative path in dir {relativePath} in  DeleteBrunnhildeFoldersAndFiles");

            if (relativePath == $"{metadataContext}/{BrunnhildeFolderName}") 
                continue;

            await DeleteFolderOrFile(depositId, relativePath, true, runUser);
        }

        await DeleteFolderOrFile(depositId, $"{metadataContext}/{BrunnhildeFolderName}", true, runUser);

    }

    private async Task DeleteFolderOrFile(string depositId, string relativePath, bool isDirectory, string? runUser)
    {
        var itemsForDeletion = new List<MinimalItem>();
        var deleteSelection = new DeleteSelection
        {
            DeleteFromDepositFiles = true,
            DeleteFromMets = true,
            Deposit = null
        };

        itemsForDeletion.Add(new MinimalItem
        {
            RelativePath = relativePath,
            IsDirectory = isDirectory,
            Whereabouts = Whereabouts.Both
        });

        var response = await preservationApiClient.GetDeposit(depositId);
        var deposit = response.Value;

        if (deposit == null)
        {
            logger.LogError($" Could not retrieve deposit for {depositId}");
            return;
        }

        workspaceManagerWorker = workspaceManagerFactory.Create(deposit);

        deleteSelection.Items = itemsForDeletion;

        var resultDelete = await workspaceManagerWorker.DeleteItems(deleteSelection, runUser ?? "PipelineApi");

        if (!resultDelete.Success)
        {
            logger.LogError($"DELETE ITEMS: Error code for relative path {relativePath}: {resultDelete.ErrorCode}");
            logger.LogError($"Error message for relative path {relativePath}: {resultDelete.ErrorMessage}");
            logger.LogError($"Error failure for relative path {relativePath}: {resultDelete.Failure}");
        }

        itemsForDeletion.Clear();
        deleteSelection.Items.Clear();
    }

    
    private async Task<(List<Result<CreateFolderResult>?> createSubFolderResult, List<Result<SingleFileUploadResult>?> uploadFileResult)> UploadFilesToMetadataRecursively(string depositId, string sourcePathForDirectories, string sourcePathForFiles, string depositPath, string? runUser)
    {
        try
        {
            var context = new StringBuilder();
            context.Append("metadata");

            logger.LogInformation($"context {context}");

            //create Brunnhilde folder first
            var createSubFolderResult = new List<Result<CreateFolderResult>?> 
            { 
                await CreateMetadataSubFolderOnS3(depositId, sourcePathForDirectories, runUser, true) 
            };

            //Now Create all of the directories
            foreach (var dirPath in Directory.GetDirectories(sourcePathForDirectories, "*", SearchOption.AllDirectories))
            {
                logger.LogInformation($"dir path {dirPath}");
                createSubFolderResult.Add(await CreateMetadataSubFolderOnS3(depositId, dirPath, runUser));
            }

            var uploadFileResult = new List<Result<SingleFileUploadResult>?>();

            foreach (var filePath in Directory.GetFiles(sourcePathForFiles, "*.*", SearchOption.AllDirectories))
            {
                logger.LogInformation($"Upload file path {filePath}");

                if (filesToIgnore.Any(filePath.Contains))
                    continue;
                
                uploadFileResult.Add(await UploadFileToDepositOnS3(depositId, filePath, sourcePathForFiles, runUser ?? "PipelineApi"));
            }

            foreach (var subFolder in createSubFolderResult)
            {
                logger.LogInformation($"subFolder.ErrorMessage {subFolder?.ErrorMessage} , subFolder?.Value?.Context {subFolder?.Value?.Context} subFolder?.Value?.Created {subFolder?.Value?.Created} hghhjhjsd ");
            }

            foreach (var uploadFile in uploadFileResult)
            {
                logger.LogInformation($" uploadFile.Value.Context {uploadFile?.Value?.Context}");
            }

            if (createSubFolderResult.Any() && uploadFileResult.Any())
            {
                return (createSubFolderResult, uploadFileResult);
            }

        }
        catch (Exception ex)
        {
            logger.LogError(ex, $" Caught error in copy files recursively from {sourcePathForFiles} to {depositPath}");
        }

        return ((List<Result<CreateFolderResult>?> createSubFolderResult, List<Result<SingleFileUploadResult>?> uploadFileResult))(Enumerable.Empty<Result<CreateFolderResult>>(), Enumerable.Empty<Result<SingleFileUploadResult>>());

    }

    private async Task<Result<CreateFolderResult>?> CreateMetadataSubFolderOnS3(string depositId, string dirPath, string? runUser, bool brunnhildeFolder = false)
    {
        var context = new StringBuilder();

        var metadataContext = "metadata";

        context.Append(metadataContext);

        var di = new DirectoryInfo(dirPath);

        var response = await preservationApiClient.GetDeposit(depositId);
        var deposit = response.Value;

        if (deposit == null)
        {
            logger.LogError($" Could not retrieve deposit for {depositId} and could not unlock");
            return null;
        }

        workspaceManagerWorker = workspaceManagerFactory.Create(deposit);

        if (di.Parent?.Name.ToLower() == BrunnhildeFolderName && !context.ToString().Contains($"/{BrunnhildeFolderName}")) //TODO
            context.Append($"/{BrunnhildeFolderName}");

        logger.LogInformation($"BrunnhildeFolderName {BrunnhildeFolderName}");
        logger.LogInformation($"di.Name {di.Name} context {context}");
        var result = await workspaceManagerWorker.CreateFolder(di.Name, context.ToString(), false, runUser ?? "PipelineApi", true);

        if (!result.Success)
        {
            logger.LogError($"Error code for dir path {dirPath}: {result.ErrorCode}");
            logger.LogError($"Error message for dir path {dirPath}: {result.ErrorMessage}");
            logger.LogError($"Error failure for dir path {dirPath}: {result.Failure}");
        }

        return result;

    }

    private async Task<Result<SingleFileUploadResult>?> UploadFileToDepositOnS3(string depositId, string filePath, string sourcePath, string? runUser)
    {
        var context = new StringBuilder();

        var metadataContext = $"metadata";

        context.Append(metadataContext);

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

        if (fi.Directory.Name.ToLower() == BrunnhildeFolderName && !context.ToString().Contains($"/{BrunnhildeFolderName}")) 
            context.Append($"/{BrunnhildeFolderName}");

        var checksum = Checksum.Sha256FromFile(fi);

        if (string.IsNullOrEmpty(checksum))
            return null;

        var stream = GetFileStream(filePath);
        var result = await UploadFileToBucketDeposit(depositId, stream, filePath, contextPath, checksum, runUser ?? "PipelineApi");


        if (!result.Success)
        {
            logger.LogError($"Error code for file path {filePath}: {result.ErrorCode}");
            logger.LogError($"Error message for file path {filePath}: {result.ErrorMessage}");
            logger.LogError($"Error failure for file path {filePath}: {result.Failure}");
        }
        else
        {
            logger.LogInformation($"uploaded file {result?.Value?.Uploaded} with context {result?.Value?.Context}");
        }

        await stream.DisposeAsync();
        return result ?? null;
    }

    private async Task<Result<SingleFileUploadResult>> UploadFileToBucketDeposit(string id, Stream stream, string filePath, string contextPath, string checksum, 
        string runUser)
    {
        var response = await preservationApiClient.GetDeposit(id);
        var deposit = response.Value;

        if (deposit == null)
        {
            logger.LogError($" Could not retrieve deposit for {id} and could not unlock");
            return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError, "deposit is null so cant upload file");
        }

        var workspaceManager = workspaceManagerFactory.Create(deposit);

        var fi = new FileInfo(filePath);

        try
        {
            MimeTypes.TryGetMimeType(filePath.GetSlug(), out var contentType);

            if(string.IsNullOrEmpty(contentType))
                return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError, "Could not find file content type");

            var result = await workspaceManager.UploadSingleSmallFile(stream, stream.Length, fi.Name, checksum, fi.Name, contentType, contextPath, runUser, true);

            return result;
        }
        catch (Exception ex)
        {
            return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError, ex.Message);
        }
    }

    public static Stream GetFileStream(string filePath)
    {
        if (!File.Exists(filePath)) throw new Exception($"{filePath} file not found.");
        Stream result = File.OpenRead(filePath);
        if (result.Length > 0)
        {
            result.Seek(0, SeekOrigin.Begin);
        }

        return result;
    }

    private async Task AddObjectsToMETS(string depositName, string depositPath, string runUser)
    {
        var (metadataPath, metadataProcessPath, objectPath) = await GetFilePaths(depositName);
        var minimalItems = new List<MinimalItem>();

        var response = await preservationApiClient.GetDeposit(depositName);
        var deposit = response.Value;

        if (deposit == null)
        {
            logger.LogError($" Could not retrieve deposit for {depositName}");
            return;
        }

        var workspaceManager = workspaceManagerFactory.Create(deposit);

        await workspaceManager.GetCombinedDirectory(true);

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

        var combinedResult = await workspaceManager.GetCombinedDirectory(true);
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

        await workspaceManager.AddItemsToMets(wbsToAdd, runUser);
    }
}