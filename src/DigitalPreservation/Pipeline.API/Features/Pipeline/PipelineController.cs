using DigitalPreservation.Common.Model.Import;
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
using System.IO;

namespace Pipeline.API.Features.Pipeline;

[ApiKey]
[Route("[controller]")]
[ApiController]
public class PipelineController(
    IMediator mediator,
    ILogger<PipelineController> logger,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions) : Controller
{
    private readonly List<string> files = [];

    [HttpPost(Name = "ExecutePipelineProcess")]
    [Produces<ProcessPipelineResult>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecutePipelineJob([FromBody] PipelineJob pipelineJob,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing pipeline process ");
        var pipelineProcessJobResult = await mediator.Send(new ProcessPipelineJob(pipelineJob), cancellationToken);
        logger.LogInformation("Returned from ProcessPipelineJob");
        return this.StatusResponseFromResult(pipelineProcessJobResult, 204); //TODO: make this the S3 bucket location
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
                await process.StandardInput.WriteLineAsync("echo \"$PWD\""); //echo hello
                var output = await process.StandardOutput.ReadLineAsync();
                //Console.WriteLine(output);

                return output;
            }
            catch (Exception e)
            {
                var s = e;
            }

            return null;

        }

        private async Task<string?> GetDf(string targetDirectory)
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
                await process.StandardInput.WriteLineAsync("echo \"$df\""); //echo hello
                var output = await process.StandardOutput.ReadLineAsync();
                //Console.WriteLine(output);

                return output;
            }
            catch (Exception e)
            {
                var s = e;
            }

            return null;

        }

}


public class DirectoryModel
{
    public List<string> FilesInTarget { get; set; }
    public string[] Directories { get; set; }
    public string? WorkingDirectory { get; set; }
    public List<string> Errors { get; set; } = new();
}

