using System.Diagnostics;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.Extensions.Options;
using Pipeline.API.ApiClients;
using Pipeline.API.Config;

namespace Pipeline.API.Features.Pipeline.Requests;

public class ExecutePipelineJob(string jobIdentifier, string depositName, string? RunUser) : IRequest<Result>
{
    public string JobIdentifier { get; } = jobIdentifier;
    public string DepositName { get; } = depositName;
    public string? RunUser { get; set; }
}

public class ProcessPipelineJobHandler(
    ILogger<ProcessPipelineJobHandler> logger,
    IOptions<StorageOptions> storageOptions,
    IOptions<BrunnhildeOptions> brunnhildeOptions,
    IPipelineJobStateLogger pipelineJobStateLogger,
    IPreservationApiInterface preservationApiInterface) : IRequestHandler<ExecutePipelineJob, Result> 
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

        var objectPath = $"{mountPath}{separator}{depositName}{separator}{objectFolder}";

        logger.LogInformation($"Object folder path value: {objectPath}");

        var metadataPath = $"{mountPath}{separator}{depositName}{separator}metadata{separator}brunnhilde";

        logger.LogInformation($"Metadata folder value: {metadataPath}");

        if (!Directory.Exists(objectPath))
        {
            logger.LogError($"Deposit {depositName} folder and contents could not be found at {objectPath}");
            logger.LogInformation($"Metadata folder");
            return;
        }

        var start = new ProcessStartInfo
        {
            FileName = brunnhildeOptions.Value.PathToPython,
            Arguments = $"  {brunnhildeOptions.Value.PathToBrunnhilde} --hash sha256 {objectPath} {metadataPath}  --overwrite ",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(start);
        using var reader = process?.StandardOutput;
        var result = reader?.ReadToEnd();

        if (!string.IsNullOrEmpty(result) && result.Contains("Brunnhilde characterization complete."))
        {
            await preservationApiInterface.MakeHttpRequestAsync<PipelineDeposit, PipelineDeposit>(
                $"Deposits/{depositName}/lock", HttpMethod.Delete, new PipelineDeposit { Id = depositName });

            await pipelineJobStateLogger.LogJobState(jobIdentifier, depositName, runUser, PipelineJobStates.Completed);
        }

    }
}