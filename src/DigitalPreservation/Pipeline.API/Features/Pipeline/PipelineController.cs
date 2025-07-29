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
using System.IO;

namespace Pipeline.API.Features.Pipeline;

[ApiKey]
[Route("[controller]")]
[ApiController]
public class PipelineController(IMediator mediator,
    ILogger<PipelineController> logger, 
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions) : Controller
{
    private readonly List<string> files = [];
    [HttpPost(Name = "ExecutePipelineProcess")]
    [Produces<ProcessPipelineResult>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecutePipelineJob([FromBody] PipelineJob pipelineJob, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing pipeline process ");
        var pipelineProcessJobResult = await mediator.Send(new ProcessPipelineJob(pipelineJob), cancellationToken);
        logger.LogInformation("Returned from ProcessPipelineJob");
        return this.StatusResponseFromResult(pipelineProcessJobResult, 204); //TODO: make this the S3 bucket location
    }

    [HttpGet(Name = "CheckDepositFolderExists")]
    [Produces<string[]>]
    [Produces("application/json")]
    public async Task<List<string>> CheckDepositFolderAndContents([FromQuery] DepositFilesModel depositFilesModel, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"Checking deposit folder {depositFilesModel.DepositNameOrPath} and contents exist.");

        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;

        var objectPath = $"{mountPath}{separator}{depositFilesModel.DepositNameOrPath}{separator}{objectFolder}";

        if (!Directory.Exists(depositFilesModel.DepositNameOrPath))
        {
            return [$"Deposit {depositFilesModel.DepositNameOrPath} objects directory and contents do not exist"]; // at {objectPath}
        }
        
        ProcessDirectory(depositFilesModel.DepositNameOrPath);

        logger.LogInformation("Returned from CheckDepositFolderExists");
        return await Task.FromResult(files);
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
            ProcessDirectory(subdirectory);
    }

}
