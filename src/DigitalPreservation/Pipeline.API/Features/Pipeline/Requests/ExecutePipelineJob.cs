using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.Extensions.Options;
using Pipeline.API.ApiClients;
using Pipeline.API.Config;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ExecutePipelineJob(string jobIdentifier, string depositName, string? runUser) : IRequest<Result>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string DepositName { get; } = depositName;
    public string? RunUser { get; set; } = runUser;
}

public class ProcessPipelineJobHandler(
    ILogger<ProcessPipelineJobHandler> logger,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions,
    IPipelineJobStateLogger pipelineJobStateLogger,
    IPreservationApiInterface preservationApiInterface,
    IAmazonS3 s3Client) : IRequestHandler<ExecutePipelineJob, Result> 
    
{
    public async Task<Result> Handle(ExecutePipelineJob request, CancellationToken cancellationToken) // Task<Result<ProcessPipelineResult>>
    {
        var jobId = request.JobIdentifier;
        var depositId = request.DepositName;
        var runUser = request.RunUser;

        try
        {
            await pipelineJobStateLogger.LogJobState(jobId, depositId, runUser, PipelineJobStates.Running);
            await ExecuteBrunnhilde(jobId, request.DepositName, runUser);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $" Caught error in PipelineJob handler for job id {jobId} and deposit {depositId}");

            await pipelineJobStateLogger.LogJobState(jobId, depositId, runUser, PipelineJobStates.CompletedWithErrors);
            return Result.FailNotNull<Result>(ErrorCodes.UnknownError, $"Could not publish pipeline job for job id {jobId} and deposit {depositId}: " + ex.Message);
        }

        return Result.Ok();
    }
     
    private async Task ExecuteBrunnhilde(string jobIdentifier, string depositName, string? runUser)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;
        var processFolder = brunnhildeOptions.Value.ProcessFolder; 

        var objectPath = $"{mountPath}{separator}{depositName}{separator}{objectFolder}";

        logger.LogInformation($"Object folder path value: {objectPath}");

        var metadataPath = $"{mountPath}{separator}{depositName}{separator}metadata";
        var metadataProcessPath = $"{processFolder}{separator}{depositName}{separator}metadata{separator}brunnhilde";
        
        logger.LogInformation($"Metadata folder value: {metadataPath}");
        logger.LogInformation($"Metadata process folder value: {metadataProcessPath}");

        if (!Directory.Exists(objectPath))
        {
            logger.LogError($"Deposit {depositName} folder and contents could not be found at {objectPath}");
            logger.LogInformation($"Metadata folder");
            return;
        }

        if (!Directory.Exists(processFolder))
            Directory.CreateDirectory(processFolder);

        if (!Directory.Exists(metadataProcessPath))
            Directory.CreateDirectory(metadataProcessPath);

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

        if (!string.IsNullOrEmpty(result) && result.Contains("Brunnhilde characterization complete."))
        {
            await DeleteS3BrunnhildeFolder(depositName); //allows to copy across the process Brunnhilde

            var metadataPathForProcess = $"{processFolder}{separator}{depositName}{separator}metadata";
            CopyFilesRecursively(metadataPathForProcess, metadataPath);

            //1. Clean up process deposit folder
            if (Directory.Exists(metadataPathForProcess))
            {
                var metadataPathForProcessDelete = $"{processFolder}{separator}{depositName}";
                Directory.Delete(metadataPathForProcessDelete, true);
            }

            await preservationApiInterface.MakeHttpRequestAsync<PipelineDeposit, PipelineDeposit>(
                $"Deposits/{depositName}/lock", HttpMethod.Delete, new PipelineDeposit { Id = depositName });

            await pipelineJobStateLogger.LogJobState(jobIdentifier, depositName, runUser, PipelineJobStates.Completed);
        }

    }

    private async Task DeleteS3BrunnhildeFolder(string depositId)
    {
        var bucketName = "dlip-pres-dev-deposits";
        var prefix = $"deposits/{depositId}/metadata/";

        var deleteObjectsRequest = new DeleteObjectsRequest
        {
            BucketName = bucketName
        };
        var request = new ListObjectsRequest
        {
            BucketName = bucketName,
            Prefix = prefix
        };

        try
        {
            var listResponse = await s3Client.ListObjectsAsync(request);
            foreach (S3Object item in listResponse.S3Objects)
            {
                if (item.Key == prefix)
                {
                    continue;
                }
                deleteObjectsRequest.AddKey(item.Key);
            }

            if (deleteObjectsRequest.Objects.Count > 0)
            {
                await s3Client.DeleteObjectsAsync(deleteObjectsRequest);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "issue deleting objects");
        }

    }

    private void CopyFilesRecursively(string sourcePath, string targetPath)
    {
        try
        {
            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $" Caught error in copy files recursively from {sourcePath} to {targetPath}");
        }

    }
}