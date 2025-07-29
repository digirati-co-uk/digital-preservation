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

namespace Pipeline.API.Features.Pipeline;

[ApiKey]
[Route("[controller]")]
[ApiController]
public class PipelineController(IMediator mediator,
    ILogger<PipelineController> logger, 
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions) : Controller
{

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
    public async Task<string[]> CheckDepositFolderAndContents([FromQuery] DepositFilesModel depositFilesModel, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"Checking deposit folder {depositFilesModel.DepositName} and contents exist.");

        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;

        var objectPath = $"{mountPath}{separator}{depositFilesModel.DepositName}{separator}{objectFolder}";

        if (!Directory.Exists(depositFilesModel.DepositName))
        {
            return [$"Deposit {depositFilesModel.DepositName} objects directory and contents do not exist"]; // at {objectPath}
        }
        
        var fileEntries1 = Directory.GetFiles(depositFilesModel.DepositName);

        logger.LogInformation("Returned from CheckDepositFolderExists");
        return await Task.FromResult(fileEntries1);
    }
}
