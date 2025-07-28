using System.Diagnostics;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.Extensions.Options;
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
            Status = "completed", //should this be processing
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

        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = brunnhildeOptions.Value.PathToPython,
            Arguments = $"  {brunnhildeOptions.Value.PathToBrunnhilde} --hash sha256 {objectPath} {metadataPath}  --overwrite ",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        using Process? process = Process.Start(start);
        using StreamReader? reader = process?.StandardOutput;
        var result = reader?.ReadToEnd();

        if (!string.IsNullOrEmpty(result) && result.Contains("Brunnhilde characterization complete."))
        {
            //SUCCESS
            //TODO: call deposit unlock api if result is success?
        }

    }

}