using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;
using Pipeline.API.Features.Pipeline.Models;
using Pipeline.API.Middleware;
using DigitalPreservation.Common.Model.Results;
using Microsoft.Extensions.Options;
using Pipeline.API.Config;
using Preservation.Client;

namespace Pipeline.API.Features.Pipeline;

[ApiKey]
[Route("[controller]")]
[ApiController]
public class PipelineController(
    ILogger<PipelineController> logger,
    IOptions<StorageOptions> storageOptions,
    IPreservationApiClient preservationApiClient,
    InProcessPipelineQueue inProcessPipelineQueue) : ControllerBase
{
    private readonly List<string>? files = [];

    /// <summary>
    /// A thin proxy over POST /deposits/{id}/pipeline on the Preservation API (issue #231): this is
    /// the way in for a caller that cannot reach the Preservation API directly, not a second producer
    /// of pipeline jobs. Forwarding here means the lock check, the default-bucket guard, and every
    /// other check RunPipelineHandler makes all apply automatically, and every job gets a
    /// PipelineRunJob row - there is exactly one place pipeline jobs are queued from. The body's
    /// optional runUser is no longer honoured: the run is attributed to whatever identity this
    /// service calls the Preservation API with. Nothing in the platform sets runUser on this
    /// endpoint today.
    /// </summary>
    [HttpPost(Name = "ExecutePipelineProcess")]
    [Produces<Result>]
    [Produces("application/json")]
    public async Task<IActionResult> ExecutePipelineJob([FromBody] PipelineJob pipelineJob,
        CancellationToken cancellationToken = default)
    {
        if(pipelineJob.DepositName == null)
           return this.ProblemFor(ErrorCodes.BadRequest, "Deposit name is required in the request.");

        if (!PreservedResource.ValidSlug(pipelineJob.DepositName, out var invalidDepositNameReason))
            return this.ProblemFor(ErrorCodes.BadRequest, $"Deposit name is not valid: {invalidDepositNameReason}");

        var depositResult = await preservationApiClient.GetDeposit(pipelineJob.DepositName, cancellationToken);
        if (depositResult.Failure && depositResult.ErrorCode != ErrorCodes.NotFound)
        {
            // Not a "not found": the Preservation API refused us (401/403), failed, or couldn't be
            // reached. Pass that through rather than misreporting it as a missing deposit.
            logger.LogWarning("ExecutePipelineJob: could not fetch deposit {DepositName}: {Error}",
                pipelineJob.DepositName, depositResult.CodeAndMessage());
            return this.StatusResponseFromResult(depositResult);
        }
        if (depositResult.Failure || depositResult.Value is null)
        {
            logger.LogWarning("ExecutePipelineJob: deposit {DepositName} not found", pipelineJob.DepositName);
            return this.ProblemFor(ErrorCodes.NotFound, $"Deposit {pipelineJob.DepositName} not found.");
        }

        logger.LogInformation(
            "ExecutePipelineJob: forwarding pipeline run for deposit {DepositName} to the Preservation API", pipelineJob.DepositName);
        var runResult = await preservationApiClient.RunPipeline(depositResult.Value, cancellationToken);
        return this.StatusResponseFromResult(runResult, 204);
    }

    /// <summary>
    /// Local development/testing only (issue #352): the Preservation API's RunPipelineHandler calls
    /// this directly over HTTP instead of publishing to SNS when FeatureFlags:UseLocalPipelineDelivery
    /// is on, mirroring how imports already work locally (Preservation API -&gt; Storage API's own
    /// POST /import -&gt; its in-process queue). Every check (the lock, the default-bucket guard, the
    /// PipelineRunJob row) has already happened in RunPipelineHandler by the time this is called -
    /// this is purely the delivery step, the same role SNS plays today. Must not be POST /pipeline:
    /// that endpoint forwards TO the Preservation API, and reusing it here would loop back into
    /// RunPipelineHandler a second time for a job it has already created and queued once.
    /// Gated behind FeatureFlags:UseLocalHostedServiceForPipeline so it 404s wherever that is off -
    /// every deployed environment - rather than being a second, unauthenticated-by-flag way to queue
    /// a job in a live system. [ApiKey] (class-level, above) still applies underneath that.
    /// </summary>
    [HttpPost("jobs", Name = "QueuePipelineJobLocally")]
    [RequireFeatureFlag("UseLocalHostedServiceForPipeline")]
    [Produces("application/json")]
    public async Task<IActionResult> QueuePipelineJob([FromBody] PipelineJobMessage pipelineJobMessage,
        CancellationToken cancellationToken = default)
    {
        if (!pipelineJobMessage.DepositName.HasText())
            return this.ProblemFor(ErrorCodes.BadRequest, "Deposit name is required in the request.");

        if (!pipelineJobMessage.JobIdentifier.HasText())
            return this.ProblemFor(ErrorCodes.BadRequest, "Job identifier is required in the request.");

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "QueuePipelineJob: enqueueing local pipeline job {JobIdentifier} for deposit {DepositName}",
                pipelineJobMessage.JobIdentifier, pipelineJobMessage.DepositName);
        }

        if (!inProcessPipelineQueue.TryQueueRequest(
                pipelineJobMessage.JobIdentifier, pipelineJobMessage.DepositName, pipelineJobMessage.RunUser))
        {
            // The queue holds 10 messages and jobs run one at a time, so this is a real, if rare,
            // local-dev condition, not a bug - refuse it straight away rather than let the caller's
            // HttpClient sit blocked for however long it takes space to free up (issue #352 review).
            logger.LogWarning(
                "QueuePipelineJob: local pipeline queue is full, refusing job {JobIdentifier} for deposit {DepositName}",
                pipelineJobMessage.JobIdentifier, pipelineJobMessage.DepositName);
            // Left as a plain string (issue #388 item 2.3): ErrorCodes has no 503 entry, and
            // ProblemFor would turn this into a 500, losing the "retry me" signal a 503 gives.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Local pipeline queue is full; try again shortly.");
        }

        return NoContent();
    }

    [HttpGet(Name = "CheckDepositFolderExists")]
    [Produces<string[]>]
    [Produces("application/json")]
    public DirectoryModel CheckDepositFolderAndContents([FromQuery] DepositFilesModel depositFilesModel,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checking deposit folder {DepositId} and contents exist.", depositFilesModel.DepositId);

        var model = new DirectoryModel();

        try
        {

            if (string.IsNullOrEmpty(depositFilesModel.DepositId))
            {
                model.Errors.Add("DepositNameOrPath is null or empty");
                return model;
            }

            if (!PreservedResource.ValidSlug(depositFilesModel.DepositId, out var invalidDepositIdReason))
            {
                model.Errors.Add($"DepositId is not valid: {invalidDepositIdReason}");
                return model;
            }

            if (storageOptions.Value.FileMountPath == null)
            {
                model.Errors.Add("File Mount path option setting is null");
                return model;
            }

            var baseDirectory = Path.GetFullPath(storageOptions.Value.FileMountPath);

            var depositPath = Path.GetFullPath(Path.Combine(baseDirectory, depositFilesModel.DepositId));

            // Path.Combine discards baseDirectory outright when DepositId is itself rooted (e.g.
            // "/etc"), and GetFullPath resolves ".." lexically with no regard to where it ends up -
            // ValidSlug above narrows the character set but does not rule out "..", so this is the
            // check that actually keeps depositPath inside the mount.
            if (!PathX.IsUnderRoot(baseDirectory, depositPath))
            {
                logger.LogWarning(
                    "Refusing to check deposit folder for DepositId {DepositId}: {DepositPath} is not under {BaseDirectory}",
                    depositFilesModel.DepositId, depositPath, baseDirectory);
                model.Errors.Add("DepositId does not resolve to a path under the file mount.");
                return model;
            }

            var allDirectories = Directory.GetDirectories(depositPath, "*", SearchOption.AllDirectories);

            ProcessDirectory(depositPath);

            model.WorkingDirectory = baseDirectory;
            model.FilesInTarget = files;
            model.Directories = allDirectories;
            model.DiskSpace = GetDiskSpace(depositPath);

            logger.LogInformation("Returned from CheckDepositFolderExists");
        }
        catch (Exception ex)
        {
            model.Errors.Add(ex.Message);
            return model;
        }

        return model;
    }

    private void ProcessDirectory(string? targetDirectory)
    {
        if(string.IsNullOrEmpty(targetDirectory))
            return;

        // Process the list of files found in the directory.
        string[] fileEntries = Directory.GetFiles(targetDirectory);
        foreach (string fileName in fileEntries)
            if (files != null)
                files.Add(fileName);

        // Recurse into subdirectories of this directory.
        string?[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);

        foreach (string? subdirectory in subdirectoryEntries)
        {
            ProcessDirectory(subdirectory);
        }

    }

    private static string GetDiskSpace(string targetDirectory)
    {
        var drive = new DriveInfo(targetDirectory);
        return $"{drive.Name}: {drive.AvailableFreeSpace} bytes free of {drive.TotalSize} bytes";
    }

}


public class DirectoryModel
{
    public List<string>? FilesInTarget { get; set; }
    public string[]? Directories { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? DiskSpace { get; set; }
    public List<string> Errors { get; set; } = new();
}

