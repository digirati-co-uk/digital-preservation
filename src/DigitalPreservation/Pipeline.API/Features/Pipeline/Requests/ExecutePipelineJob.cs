using System.Diagnostics;
using System.Text.Json;
using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.Extensions.Options;
using Pipeline.API.Aws;
using Pipeline.API.Config;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ExecutePipelineJob(string depositName, ProcessPipelineResult initialProcessPipelineResult) : IRequest<Result<ProcessPipelineResult>> //IRequest<Result<ProcessPipelineResult>>
{
    public string DepositName { get; } = depositName;
    public ProcessPipelineResult InitialProcessPipelineResult { get; } = initialProcessPipelineResult;
}

public class ProcessPipelineJobHandler(
    ILogger<ProcessPipelineJobHandler> logger,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions) : IRequestHandler<ExecutePipelineJob, Result<ProcessPipelineResult>>
{
    public async Task<Result<ProcessPipelineResult>> Handle(ExecutePipelineJob request, CancellationToken cancellationToken)
    {
        var pipelineJobResult = request.InitialProcessPipelineResult;

        try
        {
            ExecuteBrunnhilde(request.DepositName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, " Caught error in PipelineJob handler ");

            pipelineJobResult.DateFinished = DateTime.UtcNow;
            pipelineJobResult.Status = "completedWithErrors";
            pipelineJobResult.Errors = [new Error { Message = ex.Message }];
            return await Task.FromResult(Result.OkNotNull(pipelineJobResult));
        }

        return await Task.FromResult(Result.OkNotNull(new ProcessPipelineResult
        {
            Status = "completed",
            DateFinished = DateTime.UtcNow
        }));
    }
     
    private void ExecuteBrunnhilde(string depositName)
    {
        var mountPath = storageOptions.Value.FileMountPath;
        var separator = brunnhildeOptions.Value.DirectorySeparator;
        var objectFolder = brunnhildeOptions.Value.ObjectsFolder;

        var objectPath = $"{mountPath}{separator}{depositName}{separator}{objectFolder}";
        var metadataPath = $"{mountPath}{separator}{depositName}{separator}metadata{separator}brunnhilde";

        ProcessStartInfo start = new ProcessStartInfo();
        start.FileName = brunnhildeOptions.Value.PathToPython; //TODO: Put in IOptions
        start.Arguments = $" {brunnhildeOptions.Value.PathToBrunnhilde} --hash sha256 {objectPath} {metadataPath}  --overwrite "; //app/docs/LeedsPipelineObjects /app/docs/subfolder

        //TODO: when using file mount then use Relative path
        //start.Arguments = $" --hash sha256 {mountPath}/{depositName}/objects {mountPath}/metadata/brunnhilde --overwrite "; //app/docs/LeedsPipelineObjects /app/docs/subfolder
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        using Process? process = Process.Start(start);
        using StreamReader? reader = process?.StandardOutput;
        var result = reader?.ReadToEnd();
        Console.Write(result);
        //Do return
    }

}