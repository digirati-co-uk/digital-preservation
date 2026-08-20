using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.LogHelpers;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Web;
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
            }
            else
            {
                logger.LogError("Unable to fetch diff import job for deposit {ErrorDetail}", diffImportJobResult.CodeAndMessage());
                return this.StatusResponseFromResult(diffImportJobResult);
            }
        }

        Result<ImportJobResult>? checkDeposit;
        if (importJob.Deposit is null)
        {
            var message = "Import job must declare which Deposit it is for.";
            logger.LogWarning(message);
            checkDeposit = Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, message);
            return this.StatusResponseFromResult(checkDeposit);
        }
        if (importJob.Deposit.AbsolutePath != "/deposits/" + depositId)
        {
            var message = "Import job Deposit does not match the Deposit it was submitted to.";
            logger.LogWarning(message);
            checkDeposit = Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, message);
            return this.StatusResponseFromResult(checkDeposit);
        }

        var invalidBinary = importJob.BinariesToAdd.Union(importJob.BinariesToPatch)
            .FirstOrDefault(binary => !deposit.Files!.IsBaseOf(binary.Origin!));
        if (invalidBinary != null)
        {
            var message = $"Binary origin {invalidBinary.Origin} is not a child of deposit file location {deposit.Files}.";
            logger.LogWarning(message);
            checkDeposit = Result.FailNotNull<ImportJobResult>(ErrorCodes.BadRequest, message);
            return this.StatusResponseFromResult(checkDeposit);
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
    
    private static bool IsPostedDiffReference(ImportJob importJob, PathString path)
    {
        // This is when the API caller posts a reference to the diff import job rather than an _actual_ job
        // means we have to build the diff now.
        // We may want to be more flexible that this, e.g., allowing the DigitalObject to be set as part of the immediate diff execution
        if(importJob.Id!.ToString().EndsWith(path + "/diff")
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