using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline.Models;
using Pipeline.API.Features.Pipeline.Requests;
using Pipeline.API.Middleware;
using System.Diagnostics;
using DigitalPreservation.Common.Model.Results;

namespace Pipeline.API.Features.Pipeline;

[ApiKey]
[Route("[controller]")]
[ApiController]
public class PipelineController(
    IMediator mediator,
    ILogger<PipelineController> logger,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions,
    IPipelineJobStateLogger pipelineJobStateLogger,
    IIdentityMinter identityMinter) : Controller
{
    private readonly List<string> files = [];

    [HttpPost(Name = "ExecutePipelineProcess")]
    [Produces<Result>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecutePipelineJob([FromBody] PipelineJob pipelineJob,
        CancellationToken cancellationToken = default)
    {
        var jobIdentifier = identityMinter.MintIdentity(nameof(PipelineJob));

        if (pipelineJob.DepositName != null)
            await pipelineJobStateLogger.LogJobState(jobIdentifier, pipelineJob.DepositName, pipelineJob.RunUser,  PipelineJobStates.Waiting);

        pipelineJob.JobIdentifier = jobIdentifier;

        logger.LogInformation($"ExecutePipelineJob:Executing pipeline process for job id {jobIdentifier} and deposit {pipelineJob.DepositName}");
        var pipelineProcessJobResult = await mediator.Send(new ProcessPipelineJob(pipelineJob), cancellationToken);
        logger.LogInformation($"Returned from ProcessPipelineJob for job id {jobIdentifier} and deposit {pipelineJob.DepositName}");
        return this.StatusResponseFromResult(pipelineProcessJobResult, 204); 
    }

    [HttpGet(Name = "CheckDepositFolderExists")]
    [Produces<string[]>]
    [Produces("application/json")]
    public async Task<DirectoryModel> CheckDepositFolderAndContents([FromQuery] DepositFilesModel depositFilesModel,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"Checking deposit folder {depositFilesModel.DepositNameOrPath} and contents exist.");

        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;

        var objectPath = $"{mountPath}{separator}{depositFilesModel.DepositNameOrPath}{separator}{objectFolder}";

        var model = new DirectoryModel();

        try
        {
            var allDirectories = Directory.GetDirectories(depositFilesModel.DepositNameOrPath, "*", SearchOption.AllDirectories);

            var workingDirectory = await GetWorkingDirectory(depositFilesModel.DepositNameOrPath);

            ProcessDirectory(depositFilesModel.DepositNameOrPath);

            model.WorkingDirectory = workingDirectory;
            model.FilesInTarget = files;
            model.Directories = allDirectories;
            model.DiskSpace = GetDf(depositFilesModel.DepositNameOrPath);

            logger.LogInformation("Returned from CheckDepositFolderExists");
        }
        catch (Exception ex)
        {
            model.Errors.Add(ex.Message);
            return await Task.FromResult(model);
        }

        return await Task.FromResult(model);
    }

    private void ProcessDirectory(string targetDirectory)
        {
            // Process the list of files found in the directory.
            string[] fileEntries = Directory.GetFiles(targetDirectory);
            foreach (string fileName in fileEntries)
                files.Add(fileName);

            // Recurse into subdirectories of this directory.
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);

            foreach (string subdirectory in subdirectoryEntries)
            {
                ProcessDirectory(subdirectory);
            }

        }

        private async Task<string?> GetWorkingDirectory(string targetDirectory)
        {
            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        WorkingDirectory = targetDirectory
                    }
                };
                process.Start();
                await process.StandardInput.WriteLineAsync("echo \"$PWD\"");
                var output = await process.StandardOutput.ReadLineAsync();

                return output;
            }
            catch (Exception e)
            {
                logger.LogError(e, "error getting working directory");
            }

            return null;

        }

        private string GetDf(string targetDirectory)
        {
            return Bash(GetDiskSpace(), targetDirectory);
        }

        private string GetDiskSpace()
        {
            return string.Join(" ", "df");
        }

        private string Bash(string cmd, string targetDirectory)
        {
            var escapedArgs = cmd.Replace("\"", "\\\"");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = targetDirectory 
                }
            };
            process.Start();
            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return result;
        }

}


public class DirectoryModel
{
    public List<string> FilesInTarget { get; set; }
    public string[] Directories { get; set; }
    public string? WorkingDirectory { get; set; }
    public string DiskSpace { get; set; }
    public List<string> Errors { get; set; } = new();
}

