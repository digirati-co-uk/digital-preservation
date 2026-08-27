using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Mets;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Deposits.Requests;
using Preservation.API.Features.ImportJobs.Requests;
using Preservation.API.Mutation;

namespace Preservation.API.Features.ImportJobs;


[Route("deposits/{depositId}/[controller]")]
[ApiController]
public class ImportJobsController(
    ILogger<ImportJobsController> logger,
    IMediator mediator,
    ResourceMutator resourceMutator,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("diff", Name = "GetDiffImportJob")]
    [ProducesResponseType<ImportJob>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetDiffImportJob([FromRoute] string depositId)
    {
        var depositResult = await mediator.Send(new GetDeposit(depositId));
        if (depositResult.Failure)
        {
            return this.StatusResponseFromResult(depositResult);
        }
        var validationResult = await ValidateDeposit(depositResult.Value!, 0);
        if (validationResult != null) return this.StatusResponseFromResult(validationResult);
        
        var result = await mediator.Send(new GetDiffImportJob(depositResult.Value!, User));
        if (result is { Success: true, Value: not null })
        {
            result.Value.OriginalId = GetDiffUri(depositId);
            logger.LogInformation("Controller returning import job: {ImportJobSummary}", result.Value.LogSummary());
        }
        else
        {
            logger.LogError("Failed to get diff import job: {ErrorDetail}", result.CodeAndMessage());
        }
        return this.StatusResponseFromResult(result);
    }

    private Uri? GetDiffUri(string depositId)
    {
        Uri? diffUri = null;
        // Set the originally requested diff URL
        var presUri = resourceMutator.PreservationUri;
        var hostWithPort = presUri.Host;
        if (presUri.Port != 80 && presUri.Port != 443)
        {
            hostWithPort = presUri.Host + ":" + presUri.Port;
        }
        var diffRoute = Url.RouteUrl("GetDiffImportJob", 
            new { depositId }, presUri.Scheme, hostWithPort);
        if (diffRoute.HasText())
        {
            diffUri = new Uri(diffRoute.ToLowerInvariant());
        }

        return diffUri;
    }

    [HttpPost(Name = "ExecuteImportJob")]
    [ProducesResponseType<ImportJobResult>(201, "application/json")]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    [ProducesResponseType<ProblemDetails>(409, "application/json")]
    public async Task<IActionResult> ExecuteImportJob([FromRoute] string depositId, [FromBody] ImportJob importJob,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Import Jobs Controller: Executing Import Job {ImportJobSummary}", importJob.LogSummary());

        if (importJob.SuppressActivityStreamEvent
            && !configuration.GetValue<bool>("FeatureFlags:EnableMetsIdNormalisation"))
        {
            // Suppression keeps a preserved version out of the published Activity Stream, which
            // means IIIF and every other consumer is never told to rebuild. That is right for a
            // METS ID migration and wrong for almost anything else, so the flag is only honoured
            // while the migration machinery it belongs to is switched on - and a caller asking for
            // it anywhere else is refused loudly rather than silently published, which is also
            // what protects a newer client against an older API that has never heard of the flag.
            var message = "suppressActivityStreamEvent requires FeatureFlags:EnableMetsIdNormalisation "
                          + "on this API. It exists for maintenance that changes how an object is "
                          + "recorded rather than what it holds; ordinary changes must be announced.";
            logger.LogWarning("{Message} (deposit {DepositId})", message, depositId);
            return this.StatusResponseFromResult(
                Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, message));
        }
        if (!IsPostedDiffReference(importJob, Request.Path)
            && SuppressedButNotMetsOnly(importJob) is { } refusal)
        {
            // The feature flag says WHEN suppression may be used; this says WHAT FOR. Until now
            // that rule lived only in the migration tool's client-side gate, which left a window
            // between generating a diff and executing it - and left the UI checkbox trusting the
            // operator to tick it only on the right kind of job. A diff reference is checked
            // below instead, once its content exists.
            return refusal;
        }

        var depositResult = await mediator.Send(new GetDeposit(depositId), cancellationToken);
        if (depositResult.Failure)
        {
            logger.LogError("Unable to fetch deposit {DepositId}", depositId);
            return this.StatusResponseFromResult(depositResult);
        }

        var deposit = depositResult.Value!;
        var validationResult = await ValidateDeposit(deposit, 0);
        if (validationResult != null) return this.StatusResponseFromResult(validationResult);
        
        if (IsPostedDiffReference(importJob, Request.Path))
        {
            logger.LogInformation("Submitted import job is a diff reference, creating job...");
            // The posted body is about to be replaced by a freshly generated diff, so anything the
            // caller asked for that the generator does not know about has to be carried across.
            var suppressActivityStreamEvent = importJob.SuppressActivityStreamEvent;
            var diffImportJobResult = await mediator.Send(new GetDiffImportJob(deposit, User), cancellationToken);
            if (diffImportJobResult is { Success: true, Value: not null })
            {
                importJob = diffImportJobResult.Value;
                importJob.OriginalId = GetDiffUri(depositId);
                importJob.SuppressActivityStreamEvent = suppressActivityStreamEvent;
                if (SuppressedButNotMetsOnly(importJob) is { } diffRefusal)
                {
                    // A diff reference's content is only known now the diff has been generated -
                    // and this is also what closes the window between a caller looking at a
                    // METS-only diff and the deposit changing underneath them before they post it.
                    return diffRefusal;
                }
            }
            else
            {
                logger.LogError("Unable to fetch diff import job for deposit {ErrorDetail}", diffImportJobResult.CodeAndMessage());
                return this.StatusResponseFromResult(diffImportJobResult);
            }
        }

        if (JobDoesNotBelongToDeposit(importJob, depositId, deposit) is { } mismatch)
        {
            return mismatch;
        }

        var executeImportJobResult = await mediator.Send(new ExecuteImportJob(importJob, User), cancellationToken);
        return this.StatusResponseFromResult(executeImportJobResult, 201, executeImportJobResult.Value?.Id);
    }
    
    [HttpGet("results", Name = "GetImportJobResults")]
    [ProducesResponseType<List<ImportJobResult>>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetImportJobResults([FromRoute] string depositId)
    {
        var result = await mediator.Send(new GetImportJobResultsForDeposit(depositId));
        return this.StatusResponseFromResult(result);
    }


    /// <summary>
    /// Get the status of an existing ImportJobResult - the result of executing an ImportJob
    /// </summary>
    /// <param name="depositId">Deposit depositId import job is for</param>
    /// <param name="importJobId">Unique import job identifier</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Status of ImportJobResult</returns>
    [HttpGet("results/{importJobId}", Name = "GetImportJobResult")]
    [ProducesResponseType<ImportJobResult>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    public async Task<IActionResult> GetImportJobResult([FromRoute] string depositId, [FromRoute] string importJobId,
        CancellationToken cancellationToken)
    {
        var importJobResultResult = await mediator.Send(new GetImportJobResult(depositId, importJobId), cancellationToken);
        return this.StatusResponseFromResult(importJobResultResult);
    }
    
    
    private async Task<Result?> ValidateDeposit(Deposit existingDeposit, int maxCompleted)
    {
        logger.LogInformation("Validating deposit {DepositId} with maxCompleted {MaxCompleted}", existingDeposit.Id, maxCompleted);
        if (existingDeposit.Status == DepositStates.Exporting)
        {
            logger.LogWarning("Invalid: Deposit is being exported - {DepositId}", existingDeposit.Id);
            return Result.Fail(ErrorCodes.BadRequest, "Deposit is being exported");
        }
        if (existingDeposit.ArchivalGroup == null)
        {
            logger.LogWarning("Invalid: Deposit has no Archival Group - {DepositId}", existingDeposit.Id);
            return Result.Fail(ErrorCodes.BadRequest, "Deposit requires Archival Group");
        }

        var existingImportJobResultsResult = await mediator.Send(new GetImportJobResultsForDeposit(existingDeposit.Id!.GetSlug()!));
        if (existingImportJobResultsResult.Failure || existingImportJobResultsResult.Value == null)
        {
            logger.LogError("Cannot check for existing import job results - {DepositId} - {ErrorDetail}", existingDeposit.Id, existingImportJobResultsResult.CodeAndMessage());
            return Result.Fail(ErrorCodes.UnknownError, "Could not look for existing import jobs");
        }
        var notErrors = existingImportJobResultsResult.Value.Count(ijr => ijr.Status != ImportJobStates.CompletedWithErrors);
        if (notErrors > maxCompleted)
        {
            logger.LogWarning("Invalid: there are {NotErrors} existing non-error import jobs for {DepositId}", notErrors, existingDeposit.Id);
            return Result.Fail(ErrorCodes.Conflict, "There are existing import jobs for this deposit");
        }
        logger.LogInformation("Deposit {DepositId} is considered valid", existingDeposit.Id);
        return null;
    }
    
    /// <summary>
    /// The refusal to return when a job asks for Activity Stream suppression but is not the one
    /// kind of job suppression exists for; null when the job is fine.
    /// </summary>
    /// <remarks>
    /// Suppression means "this version changes how the object is recorded, not what it holds" -
    /// a METS ID migration, and nothing else. So a suppressed job must be exactly one binary
    /// patch, of a METS file, with nothing added, deleted or renamed. This is the migration
    /// tool's own client-side gate, enforced where it can no longer be raced or forgotten: a
    /// suppressed content change would preserve a real new version that IIIF is never told to
    /// rebuild from.
    /// <para>
    /// One allowance: the platform's own empty scaffold folders, <c>metadata</c> and
    /// <c>metadata/ad-hoc</c>. Creating a deposit against an Archival Group preserved before
    /// LPII-9 writes those folders into its METS (CreateDepositBase / GetDepositBase), so the
    /// migration's diff for such a group is the METS patch plus those containers - it cannot be
    /// a pure METS patch, and refusing it would leave every pre-LPII-9 group unmigrated. They
    /// hold nothing, no consumer derives anything from them, and they would be added on the
    /// group's next preservation anyway: that is bookkeeping about how the object is recorded,
    /// not a change to what it holds. Nothing else in ContainersToAdd is tolerated.
    /// </para>
    /// </remarks>
    private ActionResult? SuppressedButNotMetsOnly(ImportJob importJob)
    {
        if (!importJob.SuppressActivityStreamEvent)
        {
            return null;
        }

        string? problem = null;
        if (importJob.BinariesToAdd.Count > 0 || importJob.BinariesToDelete.Count > 0
            || importJob.BinariesToRename.Count > 0
            || importJob.ContainersToDelete.Count > 0 || importJob.ContainersToRename.Count > 0)
        {
            problem = "it adds, deletes or renames content";
        }
        else if (importJob.ContainersToAdd.Exists(c => !IsPlatformScaffoldFolder(importJob, c)))
        {
            problem = "it adds content";
        }
        else if (importJob.BinariesToPatch.Count != 1)
        {
            problem = $"it patches {importJob.BinariesToPatch.Count} binaries rather than exactly one";
        }
        else if (importJob.BinariesToPatch[0].Id?.GetSlug() is not { } slug
                 || !MetsUtils.IsMetsFile(slug))
        {
            problem = $"the binary it patches ({importJob.BinariesToPatch[0].Id}) is not a METS file";
        }

        if (problem is null)
        {
            return null;
        }
        var message = "suppressActivityStreamEvent is only for changes to how an object is "
                      + "recorded - a single METS patch, plus at most the platform's own empty "
                      + $"metadata folders - but {problem}. "
                      + "Content changes must be announced in the Activity Stream.";
        logger.LogWarning("{Message} ({ImportJobSummary})", message, importJob.LogSummary());
        return this.StatusResponseFromResult(
            Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, message));
    }

    /// <summary>
    /// Whether a container to add is one of the platform's own scaffold folders (metadata,
    /// metadata/ad-hoc), judged by its path relative to the job's Archival Group. A job that
    /// does not say which Archival Group it is for cannot make that claim, so nothing qualifies.
    /// </summary>
    private static bool IsPlatformScaffoldFolder(ImportJob importJob, Container container)
    {
        if (importJob.ArchivalGroup is null || container.Id is null)
        {
            return false;
        }
        var agPathWithSlash = importJob.ArchivalGroup.LocalPath.TrimEnd('/') + '/';
        if (!container.Id.LocalPath.StartsWith(agPathWithSlash))
        {
            return false;
        }
        var relativePath = container.Id.LocalPath[agPathWithSlash.Length..]
            .TrimEnd('/').UnEscapePathElementsNoHashes();
        return FolderNames.RemovePathPrefix(relativePath) is FolderNames.Metadata or FolderNames.MetadataAdHoc;
    }

    /// <summary>
    /// The refusal to return when the posted job's content is not the deposit's own - it names a
    /// different Deposit, no Deposit at all, or binaries from outside the deposit's file area;
    /// null when everything belongs.
    /// </summary>
    private ActionResult? JobDoesNotBelongToDeposit(ImportJob importJob, string depositId, Deposit deposit)
    {
        string? message = null;
        if (importJob.Deposit is null)
        {
            message = "Import job must declare which Deposit it is for.";
        }
        else if (importJob.Deposit.AbsolutePath != "/deposits/" + depositId)
        {
            message = "Import job Deposit does not match the Deposit it was submitted to.";
        }
        else if (importJob.BinariesToAdd.Union(importJob.BinariesToPatch)
                     .FirstOrDefault(binary => !deposit.Files!.IsBaseOf(binary.Origin!)) is { } invalidBinary)
        {
            message = $"Binary origin {invalidBinary.Origin} is not a child of deposit file location {deposit.Files}.";
        }

        if (message is null)
        {
            return null;
        }
        logger.LogWarning("{Message}", message);
        return this.StatusResponseFromResult(
            Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, message));
    }

    private static bool IsPostedDiffReference(ImportJob importJob, PathString path)
    {
        // This is when the API caller posts a reference to the diff import job rather than an _actual_ job
        // means we have to build the diff now.
        // We may want to be more flexible that this, e.g., allowing the DigitalObject to be set as part of the immediate diff execution
        // Null-safe: a body with no Id at all is not a diff reference, and flows on to the
        // "must declare which Deposit" 400 rather than a NullReferenceException 500.
        if(importJob.Id is not null && importJob.Id.ToString().EndsWith(path + "/diff")
           && importJob.ContainersToAdd.Count == 0
           && importJob.ContainersToDelete.Count == 0
           && importJob.BinariesToAdd.Count == 0
           && importJob.BinariesToDelete.Count == 0
           && importJob.BinariesToPatch.Count == 0
           && importJob.ContainersToRename.Count == 0
           && importJob.BinariesToRename.Count == 0)
        {
            return true;
        }
        return false;
    }
}