using System.Diagnostics;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Preservation.Client;
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
    IAmazonS3 s3Client,
    WorkspaceManagerFactory workspaceManagerFactory,
    IPreservationApiClient preservationApiClient
    ) : IRequestHandler<ExecutePipelineJob, Result>

{
    private WorkspaceManager WorkspaceManager { get; set; }
    private const string BrunnhildeFolderName = "brunnhilde";
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

            WorkspaceManager = workspaceManagerFactory.Create(deposit);

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositId, jobId, PipelineJobStates.Running,
                runUser ?? "PipelineApi"), cancellationToken);

            if (pipelineJobsResult?.Value?.Errors is { Length: 0 })
                logger.LogInformation($"Job {jobId} Running status logged");

            await ExecuteBrunnhilde(jobId, request.DepositName, runUser);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $" Caught error in PipelineJob handler for job id {jobId} and deposit {depositId}");

            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositId, jobId, PipelineJobStates.CompletedWithErrors,
                runUser ?? "PipelineApi"), cancellationToken);

            //TODO: record errors
            if (pipelineJobsResult?.Value?.Errors is { Length: 0 })
                logger.LogInformation($"Job {jobId} Running status CompletedWithErrors logged");

            return Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not publish pipeline job for job id {jobId} and deposit {depositId}: " + ex.Message);
        }

        return Result.Ok();
    }
     
    //TODO: break this down
    private async Task ExecuteBrunnhilde(string jobIdentifier, string depositName, string? runUser)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;

        var (metadataPath, metadataProcessPath, objectPath) = GetFilePaths(depositName);

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
        var result = reader?.ReadToEnd();

        logger.LogInformation($"Brunnhilde result {result?.Substring(0,300)}");

        if (!string.IsNullOrEmpty(result) && result.Contains("Brunnhilde characterization complete.")) //TODO: && result.Contains("Brunnhilde characterization complete.")
        {

            logger.LogInformation($"Brunnhilde creation successful");
            var metadataPathForProcess = $"{processFolder}{separator}{depositName}{separator}metadata";

            var depositPath = $"{mountPath}{separator}{depositName}";
            await DeleteBrunnhildeFoldersAndFiles(depositName, metadataPath, depositPath, runUser);

            logger.LogInformation($"metadataPathForProcess after brunnhilde process {metadataPathForProcess}");
            logger.LogInformation($"depositName after brunnhilde process {depositName}");
            var (createFolderResultList, uploadFilesResultList) = await UploadFilesToMetadataRecursively(depositName, metadataPathForProcess, depositPath, runUser ?? "PipelineApi");

            //TODO: log all the results using createFolderResultList, uploadFilesResultList

            //1. Clean up process deposit folder
            if (Directory.Exists(metadataPathForProcess))
            {
                var metadataPathForProcessDelete = $"{processFolder}{separator}{depositName}";
                Directory.Delete(metadataPathForProcessDelete, true);
            }

            var response = await preservationApiClient.GetDeposit(depositName);
            var deposit = response.Value;

            if (deposit == null)
            {
                logger.LogError($" Could not retrieve deposit for {depositName} and could not unlock");
                return;
            }

            var releaseLockResult = await preservationApiClient.ReleaseDepositLock(deposit, new CancellationToken());
            logger.LogInformation($"releaseLockResult: {releaseLockResult.Success}");
            if (releaseLockResult is { Failure: true })
            {
                logger.LogError($"Could not release lock for Job {jobIdentifier} Completed status logged");
            }

            //TODO: handle result above 
            var pipelineJobsResult = await mediator.Send(new LogPipelineJobStatus(depositName, jobIdentifier, PipelineJobStates.Completed,
                runUser ?? "PipelineApi"));

            if (pipelineJobsResult?.Value?.Errors is { Length: 0 })
                logger.LogInformation($"Job {jobIdentifier} and deposit {depositName} pipeline run Completed status logged");
        }

    }

    private (string, string, string) GetFilePaths(string depositName)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;
        var processFolder = brunnhildeOptions.Value.ProcessFolder;
        string metadataPath;
        string metadataProcessPath;
        var objectPath = $"{mountPath}{separator}{depositName}{separator}{objectFolder}";

        if (WorkspaceManager.IsBagItLayout)
        {
            metadataPath = $"{mountPath}{separator}{depositName}{separator}data{separator}metadata";
            metadataProcessPath = $"{processFolder}{separator}{depositName}{separator}data{separator}metadata{separator}{BrunnhildeFolderName}";
        }
        else
        {
            metadataPath = $"{mountPath}{separator}{depositName}{separator}metadata";
            metadataProcessPath = $"{processFolder}{separator}{depositName}{separator}metadata{separator}{BrunnhildeFolderName}";
        }

        //get paths for processing method

        if (!Directory.Exists(objectPath))
        {
            logger.LogError($"Deposit {depositName} folder and contents could not be found at {objectPath}");
            logger.LogInformation($"Metadata folder");
            return (string.Empty, string.Empty, string.Empty);
        }

        if (!Directory.Exists(processFolder))
            Directory.CreateDirectory(processFolder);

        if (!Directory.Exists(metadataProcessPath))
            Directory.CreateDirectory(metadataProcessPath);

        return (metadataPath, metadataProcessPath, objectPath);
    }

    //TODO: add a response to this
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

        WorkspaceManager = workspaceManagerFactory.Create(deposit);

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

        if (WorkspaceManager.IsBagItLayout)
            metadataContext = "data/metadata";

        foreach (var dirPath in Directory.GetDirectories(metadataPath, "*", SearchOption.AllDirectories))
        {
            if (!dirPath.Contains(BrunnhildeFolderName))
                continue;

            var relativePath = Path.GetRelativePath(
                depositPath,
                dirPath).Replace(@"\", "/").ToLower();

            logger.LogInformation($"relative path in dir {relativePath} in  DeleteBrunnhildeFoldersAndFiles");

            //delete brunnhilde folder on its own
            if (relativePath == $"{metadataContext}/{BrunnhildeFolderName}") 
                continue;

            await DeleteFolderOrFile(depositId, relativePath, true, runUser);
        }

        //delete Brunnhilde folder last
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
        //put this into a method that every iteration can use
        //TODO: Delete Brunnhilde if not already deleted
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

        WorkspaceManager = workspaceManagerFactory.Create(deposit);

        deleteSelection.Items = itemsForDeletion;

        //TODO: add proper identity
        var resultDelete = await WorkspaceManager.DeleteItems(deleteSelection, runUser ?? "PipelineApi");


        if (!resultDelete.Success)
        {
            logger.LogError($"Error code for relative path {relativePath}: {resultDelete.ErrorCode}");
            logger.LogError($"Error message for relative path {relativePath}: {resultDelete.ErrorMessage}");
            logger.LogError($"Error failure for relative path {relativePath}: {resultDelete.Failure}");
        }

        itemsForDeletion.Clear();
        deleteSelection.Items.Clear();
    }

    
    //TODO: return results
    private async Task<(List<Result<CreateFolderResult>> createSubFolderResult, List<Result<SingleFileUploadResult>> uploadFileResult)> UploadFilesToMetadataRecursively(string depositId, string sourcePath, string depositPath, string? runUser)
    {
        try
        {
            var context = new StringBuilder();
            context.Append(WorkspaceManager.IsBagItLayout ? "data/metadata" : "metadata");

            logger.LogInformation($"context {context.ToString()}");

            //Now Create all of the directories
            List<Result<CreateFolderResult>?> createSubFolderResult = new List<Result<CreateFolderResult>?>();
            foreach (var dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                logger.LogInformation($"dir path {dirPath}");
                createSubFolderResult.Add(await CreateMetadataSubFolderOnS3(depositId, dirPath, runUser));
            }

            List<Result<SingleFileUploadResult>?> uploadFileResult = new List<Result<SingleFileUploadResult>?>();
            //Copy all the files & Replaces any files with the same name
            foreach (var filePath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                logger.LogInformation($"file path {filePath}");
                uploadFileResult.Add(await UploadFileToDepositOnS3(depositId, filePath, sourcePath, runUser ?? "PipelineApi"));
            }

            foreach (var subFolder in createSubFolderResult)
            {
                logger.LogInformation($"subFolder.ErrorMessage {subFolder?.ErrorMessage} , subFolder?.Value?.Context {subFolder?.Value?.Context} subFolder?.Value?.Created {subFolder?.Value?.Created} hghhjhjsd ");
            }

            foreach (var uploadFile in uploadFileResult)
            {
                logger.LogInformation($" uploadFile.Value.Context {uploadFile?.Value?.Context}");
            }
            //TODO: return 2 results in a Tuple
            return (createSubFolderResult, uploadFileResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $" Caught error in copy files recursively from {sourcePath} to {depositPath}");
        }

        return (null, null);
    }

    private async Task<Result<CreateFolderResult>?> CreateMetadataSubFolderOnS3(string depositId, string dirPath, string? runUser)
    {
        var context = new StringBuilder();

        var metadataContext = $"metadata";

        //this is all folders under metadata or data/metadata
        if (WorkspaceManager.IsBagItLayout)
            metadataContext = "data/metadata";

        context.Append(metadataContext);

        var di = new DirectoryInfo(dirPath);

        //source path is metadata - need bruunhilde folder
        var response = await preservationApiClient.GetDeposit(depositId);
        var deposit = response.Value;

        if (deposit == null)
        {
            logger.LogError($" Could not retrieve deposit for {depositId} and could not unlock");
            return null;
        }

        WorkspaceManager = workspaceManagerFactory.Create(deposit);

        if (di.Parent.Name.ToLower() == BrunnhildeFolderName && !context.ToString().Contains($"/{BrunnhildeFolderName}")) //TODO
            context.Append($"/{BrunnhildeFolderName}");

        logger.LogInformation($"BrunnhildeFolderName {BrunnhildeFolderName}");
        logger.LogInformation($"di.Name {di.Name} context {context}");
        var result = await WorkspaceManager.CreateFolder(di.Name, context.ToString(), false, runUser ?? "PipelineApi", true);

        if (!result.Success)
        {
            logger.LogError($"Error code for dir path {dirPath}: {result.ErrorCode}");
            logger.LogError($"Error message for dir path {dirPath}: {result.ErrorMessage}");
            logger.LogError($"Error failure for dir path {dirPath}: {result.Failure}");
        }
        //logger.LogInformation($"Result created {result?.Value?.Created} blah Result context {result?.Value?.Context} blah1");
        //TODO: add run user
        return result ?? null;

    }

    //TODO: return result
    private async Task<Result<SingleFileUploadResult>?> UploadFileToDepositOnS3(string depositId, string filePath, string sourcePath, string? runUser)
    {
        var context = new StringBuilder();

        var metadataContext = $"metadata";

        //this is all folders under metadata or data/metadata
        if (WorkspaceManager.IsBagItLayout)
            metadataContext = "data/metadata";

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

        var checksum = Checksum.Sha256FromFile(fi);
        var stream = GetFileStream(filePath);
        var result = await UploadFileToBucketDeposit(depositId, stream, filePath, contextPath, checksum, runUser ?? "PipelineApi");

        if (!result.Success)
        {
            logger.LogError($"Error code for dir path {filePath}: {result.ErrorCode}");
            logger.LogError($"Error message for dir path {filePath}: {result.ErrorMessage}");
            logger.LogError($"Error failure for dir path {filePath}: {result.Failure}");
        }
        else
        {
            logger.LogInformation($"uploaded file {result?.Value?.Uploaded} with context {result?.Value?.Context}");
        }

        return result ?? null;
    }

    private async Task<Result<SingleFileUploadResult>> UploadFileToBucketDeposit(string id, Stream stream, string filePath, string contextPath, string checksum, 
        string runUser)
    {
        //TODO: Get Deposit from Pres API using id
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
            new FileExtensionContentTypeProvider().TryGetContentType(fi.Name, out var contentType);

            var result = await workspaceManager.UploadSingleSmallFile(stream, stream.Length, fi.Name, checksum, fi.Name, contentType, contextPath, runUser);

            if (result.Success) return result;

            logger.LogError($"Error code for file path {filePath}: {result.ErrorCode}");
            logger.LogError($"Error message for file path {filePath}: {result.ErrorMessage}");
            logger.LogError($"Error failure for file path {filePath}: {result.Failure}");

            return result;
        }
        catch (Exception ex)
        {
            return Result.FailNotNull<SingleFileUploadResult>(ErrorCodes.UnknownError, ex.Message);
        }
    }

    public static Stream GetFileStream(string filePath)
    {
        //TODO: close the stream??
        Stream result;
        try
        {
            if (File.Exists(filePath))
            {
                result = File.OpenRead(filePath);
                if (result.Length > 0)
                {
                    result.Seek(0, SeekOrigin.Begin);
                }

                return result;
            }
            else
            {
                throw new Exception($"{filePath} file not found.");
            }
        }
        catch (Exception ex)
        {
            throw ex;
        }

    }
}