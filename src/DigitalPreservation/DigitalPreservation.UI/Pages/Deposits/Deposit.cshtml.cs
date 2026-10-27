using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Infrastructure;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Preservation.Client;
using System.Text.Json;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel(
    IMediator mediator,
    WorkspaceManagerFactory workspaceManagerFactory,
    IPreservationApiClient preservationApiClient,
    IOptions<PreservationOptions> preservationOptions,
    IOptions<FeatureFlagOptions> featureFlags,
    IConfiguration configuration,
    ILogger<DepositModel> logger) : PageModel
{
    public required string Id { get; set; }
    public required WorkspaceManager WorkspaceManager { get; set; }
    
    public CombinedDirectory? RootCombinedDirectory { get; set; }
    public Deposit? Deposit { get; set; }
    public string? ArchivalGroupTestWarning { get; set; }
    
    public List<ProcessPipelineResult> PipelineJobResults { get; set; } = [];
    public ProcessPipelineResult? RunningPipelineJob { get; set; }

    public bool ArchivalGroupExists => Deposit is not null && Deposit.ArchivalGroupExists;

    public bool ShowPipeline => featureFlags.Value.ShowPipeline;

    /// <summary>
    /// The largest file the browser upload form will accept, mirrored server-side by
    /// Program.cs's Kestrel/FormOptions limits so an oversized request is refused rather than
    /// streamed to S3 (issue #276 item 1). Chunked/resumable upload is out of scope; the
    /// deposit's storage location is the supported route for files over this limit.
    /// </summary>
    public long MaxUploadBytes => UploadOptions.GetMaxUploadBytes(configuration);

    /// <summary>
    /// Whether to offer the METS ID migration (issue #188 step 3) from the Actions menu. Off by
    /// default: it exists so the operation can be tried on one deposit before anything is run in
    /// bulk, not as an everyday action. Preservation API gates the endpoint itself with its own
    /// EnableMetsIdNormalisation flag, so turning this on alone shows a menu item that will be
    /// refused.
    /// </summary>
    public bool ShowNormaliseMetsIds => featureFlags.Value.ShowNormaliseMetsIds;

    public int MisMatchCount { get; private set; }
    public List<(List<CombinedFile.FileMisMatch>, string)> FileMisMatches { get; set; } = [];
    public List<string> FilesWithViruses { get; set; } = [];
    public List<ImportJobResult> ImportJobResults { get; set; } = [];
    public List<LogicalRange> LogicalStructMaps { get; set; } = [];
    public List<AccessRestriction> AccessConditions { get; set; } = [];
    public List<string> RangeTypes { get; set; } = [];

    private static readonly JsonSerializerOptions CamelCaseOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    public string LogicalStructMapsJson => JsonSerializer.Serialize(LogicalStructMaps, CamelCaseOptions);
    public string RangeTypesJson => JsonSerializer.Serialize(RangeTypes, CamelCaseOptions);

    public string PhysicalFilePathsJson => JsonSerializer.Serialize(
        RootCombinedDirectory?.Flatten().Item2
            .Where(f => f.LocalPath != null && f.LocalPath.StartsWith("objects/"))
            .Select(f => f.LocalPath)
        ?? []);

    public string FileLinkRolesJson => JsonSerializer.Serialize( // NOSONAR: cannot be static — Razor Pages views require instance properties on the model
        FileLinkRoles.ProvidesUriFromKeyword.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AbsoluteUri));
    public string RecordInfoSourcesJson => JsonSerializer.Serialize( // NOSONAR: cannot be static — Razor Pages views require instance properties on the model
        preservationOptions.Value.RecordInfoSources);

    public string PhysicalFileLinksDataJson
    {
        get
        {
            var dict = new Dictionary<string, object>();
            foreach (var f in RootCombinedDirectory?.Flatten().Item2 ?? [])
            {
                var links = f.FileInMets?.Links ?? [];
                if (links.Count > 0 && f.LocalPath != null)
                    dict[f.LocalPath] = links.Select(l => new { to = l.To, role = l.Role?.AbsoluteUri }).ToList();
            }
            return JsonSerializer.Serialize(dict);
        }
    }


    public record MismatchDisplay(string FilePath, string? Differences, string? ExifDifferences);

    public IEnumerable<MismatchDisplay> MismatchDisplayItems => FileMisMatches.Select(m =>
    {
        var regular = m.Item1
            .Where(f => !f.MetadataType.Equals("exifmetadata", StringComparison.OrdinalIgnoreCase))
            .Select(DescribeField);
        var exif = m.Item1
            .Where(f => f.MetadataType.Equals("exifmetadata", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Field);
        var regularJoined = string.Join(", ", regular);
        var exifJoined = string.Join(", ", exif);
        return new MismatchDisplay(
            m.Item2,
            regularJoined.Length > 0 ? regularJoined : null,
            exifJoined.Length > 0 ? exifJoined : null);
    });

    private static string DescribeField(CombinedFile.FileMisMatch m) => m.Field.ToLower() switch
    {
        "contenttype" => $"Content type: Deposit [{m.ValueInDeposit}], Mets [{m.ValueInMets}]",
        "pronomkey" => "Pronom key",
        "formatname" => "Format name",
        "digest" => "Digest",
        "hasvirus" => "Has virus",
        "virusfound" => "Virus found",
        "virusdefinition" => "Virus definition",
        "(missing section)" => $"Missing metadata section: {m.ValueInDeposit ?? m.ValueInMets ?? "not specified"}",
        _ => "undocumented difference"
    };
    
    public bool IsLockedByOtherUser()
    {
        return Deposit?.LockedBy != null && Deposit.LockedBy.GetSlug() != User.GetCallerIdentity();
    }

    public bool CanBeWorkedOn()
    {
        if (IsLockedByOtherUser()) return false;
        return Deposit is { Active: true } && Deposit.Status != DepositStates.Exporting;
    }

    public async Task OnGet(
        [FromRoute] string id,
        [FromQuery] bool readFromStorage,
        [FromQuery] bool writeToStorage,
        CancellationToken cancellationToken)
    {
        await BindDeposit(id, cancellationToken);
        var conditionsResult = await preservationApiClient.GetAccessConditions(cancellationToken);
        if (conditionsResult.Success)
            AccessConditions = conditionsResult.Value!;
        var rangeTypesResult = await preservationApiClient.GetRangeTypes(cancellationToken);
        if (rangeTypesResult.Success && rangeTypesResult.Value!.Count > 0)
            RangeTypes = rangeTypesResult.Value;
    }

    /// <summary>
    /// Loads the deposit and its WorkspaceManager - the minimum every POST handler below needs to
    /// send its one command. Separate from <see cref="BindDeposit"/>'s GET-only view-build (issue
    /// #388 item 4.3), which every POST used to run in full just to get here.
    /// </summary>
    private async Task<bool> LoadDeposit(string id, CancellationToken cancellationToken)
    {
        Id = id;
        var getDepositResult = await mediator.Send(new GetDeposit(id), cancellationToken);
        if (!getDepositResult.Success)
        {
            TempData[TempDataKeys.Error] = getDepositResult.CodeAndMessage();
            return false;
        }

        Deposit = getDepositResult.Value!;
        WorkspaceManager = await workspaceManagerFactory.CreateAsync(Deposit);
        logger.LogInformation("In Bind deposit for deposit {Deposit} Lock date: {LockDate}, Locked by: {LockedBy} ", Deposit.Id, Deposit.LockDate, Deposit.LockedBy);
        return true;
    }

    /// <summary>
    /// The full view-build for the GET page: <see cref="LoadDeposit"/> plus the Archival Group path
    /// check, import/pipeline job history, logical structure maps and file-mismatch calculation.
    /// </summary>
    private async Task<bool> BindDeposit(string id, CancellationToken cancellationToken)
    {
        if (!await LoadDeposit(id, cancellationToken))
        {
            return false;
        }

        await CheckArchivalGroupPathValidity(cancellationToken);

        ImportJobResults = await GetImportJobResults(cancellationToken);
        (PipelineJobResults, RunningPipelineJob) = await GetRunningPipelineJob(cancellationToken);
        LogicalStructMaps = WorkspaceManager.LogicalStructures;

        PopulateCombinedDirectoryAndMismatches();

        return true;
    }

    /// <summary>
    /// Writes success or error TempData for a <see cref="Result"/> from a POST handler, instead of
    /// each one repeating the same if/else (issue #388 item 4.3). The error case always uses
    /// <see cref="TempDataKeys.Error"/>, matching every handler already here.
    /// </summary>
    private void ReportOutcome(Result result, string successMessage, string successKey = TempDataKeys.Valid)
    {
        if (result.Success)
        {
            TempData[successKey] = successMessage;
        }
        else
        {
            TempData[TempDataKeys.Error] = result.ErrorMessage;
        }
    }

    /// <summary>
    /// Warns when the deposit's declared Archival Group path doesn't yet exist and isn't a valid
    /// location to create one at - extracted from BindDeposit to keep its own cognitive complexity
    /// under the project's threshold (SonarCloud).
    /// </summary>
    private async Task CheckArchivalGroupPathValidity(CancellationToken cancellationToken)
    {
        if (Deposit!.ArchivalGroupExists || Deposit.ArchivalGroup == null || !Deposit.ArchivalGroup.GetPathUnderRoot().HasText())
        {
            return;
        }
        var testArchivalGroupResult = await mediator.Send(new TestArchivalGroupPath(Deposit.ArchivalGroup.GetPathUnderRoot()!), cancellationToken);
        if (testArchivalGroupResult.Failure)
        {
            ArchivalGroupTestWarning = $"{testArchivalGroupResult.ErrorMessage} Please check the path and enter a different, valid location under the repository root.";
        }
    }

    /// <summary>
    /// Populates the root CombinedDirectory and, for an editable deposit, its METS/workspace
    /// mismatch counts - extracted from BindDeposit (see <see cref="CheckArchivalGroupPathValidity"/>).
    /// A deposit that is exporting has nothing to compute here: its workspace is in flight.
    /// </summary>
    private void PopulateCombinedDirectoryAndMismatches()
    {
        if (Deposit!.Status == DepositStates.Exporting)
        {
            return;
        }
        var combinedResult = WorkspaceManager.GetRootCombinedDirectory();
        if (combinedResult is not { Success: true, Value: not null })
        {
            return;
        }
        RootCombinedDirectory = combinedResult.Value;
        if (!WorkspaceManager.Editable)
        {
            return;
        }
        var (mismatches, detailedMismatches) = RootCombinedDirectory.GetMisMatches();
        MisMatchCount = mismatches.Count;
        FileMisMatches = detailedMismatches;
        FilesWithViruses = RootCombinedDirectory.GetFilesWithVirus();
    }

    public async Task<IActionResult> OnPostCreateFolder(
        [FromRoute] string id,
        [FromForm] string newFolderName,
        [FromForm] string? newFolderContext,
        [FromForm] bool contextIsFile,
        CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await WorkspaceManager.CreateFolder(newFolderName, newFolderContext, contextIsFile, User.GetCallerIdentity());
            if (result.Success)
            {
                var details = result.Value!;
                TempData[TempDataKeys.Context] = details.Context;
            }
            else
            {
                TempData[TempDataKeys.Error] = result.ErrorMessage;
            }
        }
        return RedirectToPage();
    }




    public async Task<IActionResult> OnPostDeleteItems(
        [FromRoute] string id,
        [FromForm] Whereabouts? deleteFrom,
        [FromForm] string deleteSelectionObject,
        CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var deleteSelection = JsonSerializer.Deserialize<DeleteSelection>(deleteSelectionObject)!;
            if (deleteFrom is Whereabouts.Both or Whereabouts.Deposit)
            {
                deleteSelection.DeleteFromDepositFiles = true;
            }
            if (deleteFrom is Whereabouts.Both or Whereabouts.Mets)
            {
                deleteSelection.DeleteFromMets = true;
            }
            var result = await WorkspaceManager.DeleteItems(deleteSelection, User.GetCallerIdentity());
            if (result.Success)
            {
                var details = result.Value!;
                TempData[TempDataKeys.Deleted] = $"{details.Items.Count} item(s) DELETED.";
                return Redirect($"/deposits/{id}");
            }

            TempData[TempDataKeys.Error] = result.ErrorMessage;
        }

        return RedirectToPage();
    }
    
    
    public async Task<IActionResult> OnPostAddItemsToMets(
        [FromRoute] string id,
        [FromForm] string addToMetsObject,
        CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var minimalItems = JsonSerializer.Deserialize<List<MinimalItem>>(addToMetsObject)!;
            var combinedResult = await WorkspaceManager.RefreshCombinedDirectory();
            if (combinedResult is not { Success: true, Value: not null })
            {
                TempData[TempDataKeys.Error] = "Could not read deposit file system.";
                return Redirect($"/deposits/{id}");
            }
            var contentRoot = combinedResult.Value;
            var wbsToAdd = minimalItems
                .Select(m => m.ResolveFromContentRoot(contentRoot))
                .OfType<WorkingBase>()
                .ToList();
            var result = await WorkspaceManager.AddItemsToMets(wbsToAdd, User.GetCallerIdentity());
            if (result.Success)
            {
                return Redirect($"/deposits/{id}");
            }

            TempData[TempDataKeys.Error] = result.ErrorMessage;
        }

        return RedirectToPage();
    }



    public async Task<IActionResult> OnPostUploadFile(
        [FromRoute] string id,
        [FromForm] UploadFileFormModel form,
        CancellationToken cancellationToken)
    {
        string errorMessage = "";
        if (form.DepositFile.Count == 0)
        {
            errorMessage += "No file uploaded in form. ";
        }

        if (form.DepositFile.Count > 1)
        {
            errorMessage += "More than one file uploaded in form.";
        }

        if (form.Checksum.IsNullOrWhiteSpace())
        {
            errorMessage += "No checksum supplied in form.";
        }

        if (errorMessage.HasText())
        {
            TempData[TempDataKeys.Error] = errorMessage;
            return Redirect($"/deposits/{id}");
        }

        if (form.ContextIsFile && form.NewFileContext.HasText())
        {
            form.NewFileContext = form.NewFileContext.GetParent();
        }

        var slug = PreservedResource.MakeValidSlug(form.DepositFile[0].FileName);

        if(string.IsNullOrWhiteSpace(form.DepositFileContentType) && MimeTypes.TryGetMimeType(slug, out var foundMimeType))
            form.DepositFileContentType = foundMimeType;

        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await WorkspaceManager.UploadSingleSmallFile(
                form.DepositFile[0].OpenReadStream(),
                form.DepositFile[0].Length,
                form.DepositFile[0].FileName,
                form.Checksum,
                form.DepositFileName,
                form.DepositFileContentType,
                form.NewFileContext,
                User.GetCallerIdentity()
            );
            if (result.Success)
            {
                TempData[TempDataKeys.Context] = result.Value!.Context;
            }
            else
            {
                TempData[TempDataKeys.Error] = result.ErrorMessage;
            }
        }

        return RedirectToPage();

    }
    // This is only for small files! one at a time.
    // https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0




    public async Task<IActionResult> OnPostRebuildDepositFileSystem([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await WorkspaceManager.RebuildDepositFileSystem();
            ReportOutcome(result, "View of storage has been updated.");
        }

        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostNormaliseMetsIds([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (!ShowNormaliseMetsIds)
        {
            TempData[TempDataKeys.Error] = "METS ID normalisation is not enabled.";
            return Redirect($"/deposits/{id}");
        }

        // A plain read, not BindDeposit/LoadDeposit: only the ETag is needed, and normalising reads
        // the METS for itself. The ETag is what the user was looking at when they clicked, so an
        // edit someone else saves in between comes back as a 409 rather than being overwritten.
        var depositResult = await mediator.Send(new GetDeposit(id), cancellationToken);
        if (depositResult is not { Success: true, Value: not null })
        {
            TempData[TempDataKeys.Error] = depositResult.ErrorMessage;
            return Redirect($"/deposits/{id}");
        }

        var result = await mediator.Send(new NormaliseMetsIds(id, depositResult.Value.MetsETag), cancellationToken);
        if (result is { Success: true, Value: not null })
        {
            var report = result.Value;
            // Say which of the two outcomes it was. "Nothing to do" is the answer for most
            // deposits, and is not the same as "done" - it is the answer that means this Archival
            // Group needs no new version.
            var message = report.Changed
                ? $"METS IDs normalised: {report.IdsRewritten} ID(s) and " +
                  $"{report.ReferencesRewritten} reference(s) rewritten. " +
                  "Generate an import job to preserve the change."
                : "METS IDs already conform - nothing was changed.";

            if (report.Warnings.Count > 0)
            {
                logger.LogWarning("Normalising METS IDs for deposit {DepositId} produced warnings: {Warnings}",
                    id, string.Join("; ", report.Warnings));
                // A warning can quote an ID taken verbatim from the document; <status-message>
                // HTML-encodes it along with the rest of this message when it renders the banner.
                message += $" {report.Warnings.Count} warning(s), the first being: " +
                           report.Warnings[0];
            }

            TempData[TempDataKeys.Valid] = message;
        }
        else
        {
            // Can quote an ID verbatim from the document - the duplicate-ID refusal does exactly
            // that, and an XML ID can contain markup via entity escapes. <status-message>
            // HTML-encodes this when it renders the banner.
            TempData[TempDataKeys.Error] = result.ErrorMessage;
        }

        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostValidateStorage([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await WorkspaceManager.ValidateDepositFileSystem();
            if (result.Success)
            {
                TempData[TempDataKeys.Valid] = "Storage validation succeeded. The Deposit File System file reflects S3 content.";
            }
            else
            {
                // Can quote an S3 file path verbatim (see WorkspaceManager.FindFirstDifference).
                // <status-message> HTML-encodes this when it renders the banner.
                TempData[TempDataKeys.Error] = result.ErrorMessage;
            }

        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLock([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await mediator.Send(new LockDeposit(Deposit!), cancellationToken);
            ReportOutcome(result, "Deposit locked.");
        }
        else
        {
            TempData[TempDataKeys.Error] = "Could not bind deposit on lock";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRunPipeline([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            // Preservation API takes the deposit's lock itself when it queues the run - transferring
            // it from the caller's own lock if they already held one - and releases it when the run
            // ends, whichever way that happens (issue #299). No separate LockDeposit call needed.
            var result = await mediator.Send(new RunPipeline(Deposit!), cancellationToken);
            ReportOutcome(result, "Pipeline run started; the deposit is locked until it finishes.");
        }
        else
        {
            TempData[TempDataKeys.Error] = "Could not bind deposit on run pipeline";
        }

        return RedirectToPage();
    }

    //ForceCompletePipelineRun
    public async Task<IActionResult> OnPostForceCompletePipelineRun([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            // Needs the running pipeline job's id, unlike every other handler here - LoadDeposit
            // alone isn't enough, but the full GET view-build's other work still isn't needed.
            (_, RunningPipelineJob) = await GetRunningPipelineJob(cancellationToken);
            if (RunningPipelineJob?.JobId != null)
            {
                // Posting completedWithErrors below is itself what releases the lock now, when the
                // run's own user still holds it (issue #299) - no separate ReleaseLock call needed.
                var result = await mediator.Send(new ForceCompletePipeline(RunningPipelineJob.JobId, id, User), cancellationToken);
                ReportOutcome(result, "Force complete of pipeline succeeded.");
            }
            else
            {
                TempData[TempDataKeys.Error] = "There is no running pipeline job to force-complete.";
            }
        }
        else
        {
            TempData[TempDataKeys.Error] = "Could not bind deposit on force complete of pipeline";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReleaseLock([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await mediator.Send(new ReleaseLock(Deposit!), cancellationToken);
            ReportOutcome(result, "Lock released.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteDeposit([FromRoute] string id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteDeposit(id), cancellationToken);
        if (result.Success)
        {
            TempData[TempDataKeys.Deleted] = $"Deposit {id} successfully deleted.";
            return Redirect($"/deposits");
        }
        TempData[TempDataKeys.Error] = result.CodeAndMessage();
        var getDepositResult = await mediator.Send(new GetDeposit(id), cancellationToken);
        if (getDepositResult.Success)
        {
            // We can redirect to the deposits page
            return RedirectToPage();
        }
        return RedirectToPage("/Deposits/Index");
    }
    
    public async Task<IActionResult> OnPostUpdateProperties(
        [FromRoute] string id,
        [FromForm] string? agPathUnderRoot,
        [FromForm] string? agName,
        [FromForm] string? submissionText,
        CancellationToken cancellationToken)
    {
        if (agPathUnderRoot.HasText() && agPathUnderRoot.StartsWith("http"))
        {
            var pathUrl = new Uri(agPathUnderRoot);
            agPathUnderRoot = pathUrl.AbsolutePath.ToLowerInvariant();
            if (agPathUnderRoot.StartsWith("/browse/"))
            {
                agPathUnderRoot = agPathUnderRoot.Substring("/browse/".Length);
            }
            if (agPathUnderRoot.StartsWith("/repository/"))
            {
                agPathUnderRoot = agPathUnderRoot.Substring("/repository/".Length);
            }
            if (agPathUnderRoot.StartsWith('/'))
            {
                agPathUnderRoot = agPathUnderRoot.Substring(1);
            }
        }
        var getDepositResult = await mediator.Send(new GetDeposit(id), cancellationToken);
        if (getDepositResult.Success)
        {
            var deposit = getDepositResult.Value;
            deposit!.SubmissionText = submissionText;
            // feels like this URI should not be constructed here
            if (agPathUnderRoot.HasText())
            {
                deposit.ArchivalGroup = new Uri($"{preservationOptions.Value.Root}{PreservedResource.BasePathElement}/{agPathUnderRoot}");
            }
            deposit.ArchivalGroupName = agName;
            var saveDepositResult = await mediator.Send(new UpdateDeposit(deposit), cancellationToken);
            if (saveDepositResult.Success)
            {
                TempData[TempDataKeys.Updated] = "Deposit successfully updated";
                return Redirect($"/deposits/{id}");
            }
            TempData[TempDataKeys.Error] = saveDepositResult.CodeAndMessage();
        }
        else
        {
            TempData[TempDataKeys.Error] = getDepositResult.CodeAndMessage();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetRightsAndAccess(
        [FromRoute] string id,
        [FromForm] SetRightsAndAccessFormModel form,
        CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            List<FileLink>? fileLinks = null;
            if (form.ModsContextIsFile && !string.IsNullOrEmpty(form.FileLinksJson))
            {
                try { fileLinks = JsonSerializer.Deserialize<List<FileLink>>(form.FileLinksJson); }
                catch { /* ignore malformed */ }
            }

            // The rights dropdown distinguishes three choices:
            //   ""        → clear any explicit rights and inherit from the parent
            //   sentinel  → explicitly no rights; stop inheriting (effective null)
            //   a URI     → set that rights statement
            var suppressRights = form.RightsStatement == RightsStatement.DoNotInheritSentinel;
            Uri? rightsUri = null;
            if (!suppressRights)
            {
                Uri.TryCreate(form.RightsStatement, UriKind.Absolute, out rightsUri);
            }
            var result = await WorkspaceManager.SetModsInformation(form.ModsContext, form.AccessRestrictions, rightsUri, suppressRights, form.RecordIdentifiers, fileLinks);
            ReportOutcome(result, "Access Restrictions and Rights Statement updated.", TempDataKeys.AccessConditionsUpdated);
        }

        return RedirectToPage();
    }


    public string? GetDisplayTitle()
    {
        return Deposit == null ? Id : Deposit.GetDisplayTitle();
    }
    
    public async Task<List<ImportJobResult>> GetImportJobResults(CancellationToken cancellationToken)
    {
        var fetchResultsResult = await DepositJobResultFetcher.GetImportJobResults(Id, mediator, cancellationToken);
        if (fetchResultsResult.Success)
        {
            var importJobResults = fetchResultsResult.Value!;
            return importJobResults;
        }

        TempData[TempDataKeys.Error] = fetchResultsResult.CodeAndMessage();
        return [];
    }

    public async Task<List<ProcessPipelineResult>> GetPipelineJobResults(CancellationToken cancellationToken)
    {
        var fetchResultsResult = await DepositJobResultFetcher.GetPipelineJobResults(Id, mediator, cancellationToken);
        if (fetchResultsResult.Success)
        {
            var pipelineJobResults = fetchResultsResult.Value!;
            return pipelineJobResults;
        }

        TempData[TempDataKeys.Error] = fetchResultsResult.CodeAndMessage();
        return [];
    }

    public string GetDepositLocation()
    {
        if (Deposit != null && Deposit.Files?.Scheme == "s3")
        {
            const string template =
                "https://eu-west-1.console.aws.amazon.com/s3/buckets/{bucket}?region=eu-west-1&bucketType=general&prefix={prefix}&showversions=false";
            var s3Uri = new AmazonS3Uri(Deposit.Files);
            string href = template
                .Replace("{bucket}", s3Uri.Bucket)
                .Replace("{prefix}", s3Uri.Key.TrimEnd('/') + "/");
            return href;
        }

        return "#";
    }


    /// <summary>
    /// Finds this deposit's currently-running (or waiting) pipeline job, if any, for display. Used
    /// to tidy up stalled jobs itself as a side effect of this GET - that cleanup is now a
    /// Preservation API background sweep (issue #301), run on a timer regardless of whether anyone
    /// opens this page, so a deposit can't be unstuck twice by two people opening it at once, and it
    /// no longer released a lock this page view had no business touching.
    /// </summary>
    /// <remarks>
    /// Used to pick the latest-by-DateBegun job first and only then ask whether that one was still
    /// running - so a job stuck non-terminal that was not the latest (DateBegun null, or earlier than
    /// a job started since) was invisible here, with nothing to offer "Force complete" on, even
    /// though it was exactly what was blocking RunPipelineHandler's own check (issue #357). Picks the
    /// running job directly instead: any non-terminal job for this deposit, most recent by Created
    /// (the job's DateSubmitted - see ResourceMutator.MutatePipelineRunJob) in the rare case more than
    /// one somehow exists. RunPipelineHandler blocks on any active job regardless of which is latest
    /// (issue #357 review - #353's partial unique index allows at most one active job per deposit
    /// anyway, so in steady state there is at most one for this to find), and
    /// PipelineJobCleanupProcessor closes a non-latest one immediately - so a job this shows as
    /// running is always exactly the one the 409 and the sweep already agree is active.
    /// </remarks>
    public async Task<(List<ProcessPipelineResult> jobs, ProcessPipelineResult? runningJob)> GetRunningPipelineJob(CancellationToken cancellationToken)
    {
        var allJobs = await GetPipelineJobResults(cancellationToken);

        var runningJob = allJobs
            .Where(x => x.Deposit == Id && PipelineJobStates.IsNotComplete(x.Status))
            .OrderByDescending(x => x.Created)
            .FirstOrDefault();

        return (allJobs, runningJob);
    }

    public async Task<IActionResult> OnPostActivateDeposit([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await mediator.Send(new ActivateDeposit(Deposit!), cancellationToken);
            ReportOutcome(result, "Activated Deposit.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateDeposit([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await mediator.Send(new DeactivateDeposit(Deposit!), cancellationToken);
            ReportOutcome(result, "Deactivated Deposit.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateLogicalStructMap([FromRoute] string id, CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var newId = $"LOG_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var stub = new LogicalRange { Id = newId, Type = "Collection", Name = "Logical" };
            var result = await WorkspaceManager.SetLogicalStructMap(stub);
            if (result.Success)
            {
                TempData[TempDataKeys.ActiveTab] = newId;
            }
            ReportOutcome(result, "Logical structure created.", TempDataKeys.Updated);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveLogicalStructMap(
        [FromRoute] string id,
        [FromForm] string logicalStructMapJson,
        CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            LogicalRange? logicalRange;
            try
            {
                logicalRange = JsonSerializer.Deserialize<LogicalRange>(logicalStructMapJson, CaseInsensitiveOptions);
            }
            catch (JsonException ex)
            {
                TempData[TempDataKeys.Error] = "Could not parse logical structure: " + ex.Message;
                return Redirect($"/deposits/{id}");
            }

            if (logicalRange == null)
            {
                TempData[TempDataKeys.Error] = "Logical structure was empty.";
                return Redirect($"/deposits/{id}");
            }

            var result = await WorkspaceManager.SetLogicalStructMap(logicalRange);
            if (result.Success)
            {
                TempData[TempDataKeys.ActiveTab] = logicalRange.Id;
            }
            ReportOutcome(result, "Logical structure saved.", TempDataKeys.Updated);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteLogicalStructMap(
        [FromRoute] string id,
        [FromForm] string structMapId,
        CancellationToken cancellationToken)
    {
        if (await LoadDeposit(id, cancellationToken))
        {
            var result = await WorkspaceManager.RemoveLogicalStructMap(structMapId);
            ReportOutcome(result, "Logical structure deleted.", TempDataKeys.Updated);
        }
        return RedirectToPage();
    }
}

/// <summary>
/// The fields posted by the "Upload File" form on the deposit page, grouped into one bound model
/// (SonarCloud: OnPostUploadFile had 8 parameters, 1 over the project's limit). ASP.NET Core's
/// complex-type model binder tries the parameter name as a prefix first (e.g. "form.DepositFile")
/// and falls back to the bare property name when that finds nothing, so the existing posted field
/// names (depositFile, checksum, etc. - unchanged in the view) still bind correctly with no view
/// or JS changes needed.
/// </summary>
public class UploadFileFormModel
{
    public List<IFormFile> DepositFile { get; set; } = [];
    public string Checksum { get; set; } = "";
    public string DepositFileName { get; set; } = "";
    public string DepositFileContentType { get; set; } = "";
    public string? NewFileContext { get; set; }
    public bool ContextIsFile { get; set; }
}

/// <summary>
/// The fields posted by the "Set Rights and Access" form on the deposit page, grouped into one
/// bound model for the same reason as <see cref="UploadFileFormModel"/> - including
/// <see cref="RecordIdentifiers"/>, posted as indexed sub-properties (e.g.
/// <c>RecordIdentifiers[0].Source</c>) by deposit.js: the same prefix-fallback binding applies to
/// each property in turn, so the nested indexer names still resolve once the top-level prefix
/// ("form") itself falls through to none.
/// </summary>
public class SetRightsAndAccessFormModel
{
    public string ModsContext { get; set; } = "";
    public bool ModsContextIsFile { get; set; }
    public List<string> AccessRestrictions { get; set; } = [];
    public string? RightsStatement { get; set; }
    public RecordIdentifier[] RecordIdentifiers { get; set; } = [];
    public string? FileLinksJson { get; set; }
}

