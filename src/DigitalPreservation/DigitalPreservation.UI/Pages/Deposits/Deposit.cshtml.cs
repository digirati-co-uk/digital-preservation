using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Preservation.Client;
using System.Text.Json;

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel(
    IMediator mediator, 
    IOptions<PreservationOptions> options,
    WorkspaceManagerFactory workspaceManagerFactory,
    IPreservationApiClient preservationApiClient,
    IOptions<PipelineOptions> pipelineOptions,
    ILogger<DepositModel> logger,
    IIdentityMinter identityMinter,
    IConfiguration configuration) : PageModel
{
    public required string Id { get; set; }
    public required WorkspaceManager WorkspaceManager { get; set; }
    
    public CombinedDirectory? RootCombinedDirectory { get; set; }
    public Deposit? Deposit { get; set; }
    public string? ArchivalGroupTestWarning { get; set; }
    
    // NB there is no equivalent ImportJobResults at the Model level because we lazily load it
    // Whereas for pipeline jobs we need to know if there are any up front.
    public List<ProcessPipelineResult> PipelineJobResults { get; set; } = [];
    public ProcessPipelineResult? RunningPipelineJob { get; set; }

    public bool ArchivalGroupExists => Deposit is not null && Deposit.ArchivalGroupExists;

    public bool ShowPipeline => configuration.GetValue<bool?>("FeatureFlags:ShowPipeline") ?? false;

    public async Task OnGet(
        [FromRoute] string id,
        [FromQuery] bool readFromStorage = false,
        [FromQuery] bool writeToStorage = false)
    {
        await BindDeposit(id, readFromStorage, writeToStorage);
    }

    private async Task<bool> BindDeposit(string id, bool readFromStorage = false, bool writeToStorage = false)
    {
        Id = id;
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            Deposit = getDepositResult.Value!;
            WorkspaceManager = await workspaceManagerFactory.CreateAsync(Deposit);
            
            if (!Deposit.ArchivalGroupExists && Deposit.ArchivalGroup != null && Deposit.ArchivalGroup.GetPathUnderRoot().HasText())
            {
                var testArchivalGroupResult = await mediator.Send(new TestArchivalGroupPath(Deposit.ArchivalGroup.GetPathUnderRoot()!));
                if (testArchivalGroupResult.Failure)
                {
                    ArchivalGroupTestWarning = testArchivalGroupResult.ErrorMessage;
                }
            }
            
            (PipelineJobResults, RunningPipelineJob) = await GetCleanedPipelineJobsRunning();

            if (Deposit.Status != DepositStates.Exporting)
            {
                var combinedResult = WorkspaceManager.GetRootCombinedDirectory();
                if (combinedResult is { Success: true, Value: not null })
                {
                    RootCombinedDirectory = combinedResult.Value;
                    if (WorkspaceManager.Editable)
                    {
                        var mismatches = RootCombinedDirectory.GetMisMatches();
                        if (mismatches.Count != 0)
                        {
                            TempData["MisMatchCount"] = mismatches.Count;
                        }
                    }
                }
            }
        }
        else
        {
            TempData["Error"] = getDepositResult.CodeAndMessage();
            return false;
        }

        return true;
    }
    

    public async Task<IActionResult> OnPostCreateFolder(
        [FromRoute] string id, 
        [FromForm] string newFolderName,
        [FromForm] string? newFolderContext,
        [FromForm] bool contextIsFile)
    {
        if (await BindDeposit(id))
        {
            var result = await WorkspaceManager.CreateFolder(newFolderName, newFolderContext, contextIsFile, User.GetCallerIdentity());
            if (result.Success)
            {
                var details = result.Value!;
                TempData["Created"] = details.Created;
                TempData["Context"] = details.Context;
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }
        return Redirect($"/deposits/{id}");
    }
    
    
    

    public async Task<IActionResult> OnPostDeleteItems(
        [FromRoute] string id,
        [FromForm] Whereabouts? deleteFrom,
        [FromForm] string deleteSelectionObject)
    {
        if (await BindDeposit(id))
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
                TempData["Deleted"] = $"{details.Items.Count} item(s) DELETED.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }
        
        return Redirect($"/deposits/{id}");
    }
    
    
    public async Task<IActionResult> OnPostAddItemsToMets(
        [FromRoute] string id,
        [FromForm] string addToMetsObject)
    {
        if (await BindDeposit(id))
        {
            var minimalItems = JsonSerializer.Deserialize<List<MinimalItem>>(addToMetsObject)!;
            var combinedResult = await WorkspaceManager.RefreshCombinedDirectory();
            if (combinedResult is not { Success: true, Value: not null })
            {
                TempData["Error"] = "Could not read deposit file system.";
                return Redirect($"/deposits/{id}");
            }
            var wbsToAdd = new List<WorkingBase>();
            var contentRoot = combinedResult.Value;
            foreach (var item in minimalItems)
            {
                WorkingBase? wbToAdd = item.IsDirectory
                    ? contentRoot.FindDirectory(item.RelativePath)?.DirectoryInDeposit?.ToRootLayout()
                    : contentRoot.FindFile(item.RelativePath)?.FileInDeposit?.ToRootLayout();
                if (wbToAdd != null)
                {
                    wbsToAdd.Add(wbToAdd);
                }
            }
            var result = await WorkspaceManager.AddItemsToMets(wbsToAdd, User.GetCallerIdentity());
            if (result.Success)
            {
                var details = result.Value!;
                TempData["Created"] = $"{details.Items.Count} item(s) added to METS.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }
        
        return Redirect($"/deposits/{id}");
    }



    public async Task<IActionResult> OnPostUploadFile(
        [FromRoute] string id,
        [FromForm] List<IFormFile> depositFile,
        [FromForm] string checksum,
        [FromForm] string depositFileName,
        [FromForm] string depositFileContentType,
        [FromForm] string? newFileContext,
        [FromForm] bool contextIsFile)
    {
        string errorMessage = "";
        if (depositFile.Count == 0)
        {
            errorMessage += "No file uploaded in form. ";
        }

        if (depositFile.Count > 1)
        {
            errorMessage += "More than one file uploaded in form.";
        }

        if (checksum.IsNullOrWhiteSpace())
        {
            errorMessage += "No checksum supplied in form.";
        }

        if (errorMessage.HasText())
        {
            TempData["Error"] = errorMessage;
            return Redirect($"/deposits/{id}");
        }

        if (contextIsFile && newFileContext.HasText())
        {
            newFileContext = newFileContext.GetParent();
        }

        if (await BindDeposit(id))
        {
            var result = await WorkspaceManager.UploadSingleSmallFile(
                depositFile[0].OpenReadStream(),
                depositFile[0].Length,
                depositFile[0].FileName,
                checksum,
                depositFileName,
                depositFileContentType,
                newFileContext,
                User.GetCallerIdentity()
            );
            if (result.Success)
            {
                var details = result.Value!;
                TempData["Uploaded"] = details.Uploaded;
                TempData["Context"] = details.Context;
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }

        return Redirect($"/deposits/{id}");

    }
    // This is only for small files! one at a time.
    // https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0




    public async Task<IActionResult> OnPostRebuildDepositFileSystem([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await WorkspaceManager.RebuildDepositFileSystem();
            if (result.Success)
            {
                TempData["Valid"] = "View of storage has been updated.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }

        return Redirect($"/deposits/{id}");
    }


    public async Task<IActionResult> OnPostValidateStorage([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await WorkspaceManager.ValidateDepositFileSystem();
            if (result.Success)
            {
                TempData["Valid"] = "Storage validation succeeded. The Deposit File System file reflects S3 content.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }

        }

        return Redirect($"/deposits/{id}");
    }
    
    public async Task<IActionResult> OnPostLock([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await mediator.Send(new LockDeposit(Deposit!));
            if (result.Success)
            {
                TempData["Valid"] = "Deposit locked.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }
        else
        {
            TempData["Error"] = "Could not bind deposit on lock";
        }

        return Redirect($"/deposits/{id}");
    }

    public async Task<IActionResult> OnPostRunPipeline([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await mediator.Send(new LockDeposit(Deposit!));
            var result1 = await mediator.Send(new RunPipeline(Deposit!));

            if (result.Success && result1.Success)
            {
                TempData["Valid"] = "Deposit locked and pipeline run message sent.";
                TempData.Remove("MisMatchCount"); //will be recalculated as METS is refreshed with pipeline run
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }
        else
        {
            TempData["Error"] = "Could not bind deposit on run pipeline";
        }

        return Redirect($"/deposits/{id}");
    }

    //ForceCompletePipelineRun
    public async Task<IActionResult> OnPostForceCompletePipelineRun([FromRoute] string id)
    {
        if (await BindDeposit(id) && RunningPipelineJob?.JobId != null)
        {
            var result = await mediator.Send(new ReleaseLock(Deposit!));
            var result1 = await mediator.Send(new ForceCompletePipeline(RunningPipelineJob.JobId, id, User));
            if (result.Success && result1.Success)
            {
                TempData["Valid"] = "Force complete of pipeline succeeded and lock released.";
            }
            else
            {
                TempData["Error"] = result1.ErrorMessage;
            }
        }
        else
        {
            TempData["Error"] = "Could not bind deposit on force complete of pipeline";
        }

        return Redirect($"/deposits/{id}");
    }

    public async Task<IActionResult> OnPostReleaseLock([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await mediator.Send(new ReleaseLock(Deposit!));
            if (result.Success)
            {
                TempData["Valid"] = "Lock released.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }

        return Redirect($"/deposits/{id}");
    }


    public async Task<IActionResult> OnPostDeleteDeposit([FromRoute] string id)
    {
        var result = await mediator.Send(new DeleteDeposit(id));
        if (result.Success)
        {
            TempData["Deleted"] = $"Deposit {id} successfully deleted.";
            return Redirect($"/deposits");
        }
        TempData["Error"] = result.CodeAndMessage();
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            // We can redirect to the deposits page
            return Redirect($"/deposits/{id}");
        }
        return Redirect($"/deposits");
    }
    
    public async Task<IActionResult> OnPostUpdateProperties(
        [FromRoute] string id,
        [FromForm] string? agPathUnderRoot,
        [FromForm] string? agName,
        [FromForm] string? submissionText)
    {
        if (agPathUnderRoot.HasText())
        {
            if (agPathUnderRoot.StartsWith("http"))
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
                if (agPathUnderRoot.StartsWith("/"))
                {
                    agPathUnderRoot = agPathUnderRoot.Substring(1);
                }
            }
        }
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            var deposit = getDepositResult.Value;
            deposit!.SubmissionText = submissionText;
            // feels like this URI should not be constructed here
            if (agPathUnderRoot.HasText())
            {
                deposit.ArchivalGroup = new Uri($"{options.Value.Root}{PreservedResource.BasePathElement}/{agPathUnderRoot}");
            }
            deposit.ArchivalGroupName = agName;
            var saveDepositResult = await mediator.Send(new UpdateDeposit(deposit));
            if (saveDepositResult.Success)
            {
                TempData["Updated"] = "Deposit successfully updated";
                return Redirect($"/deposits/{id}");
            }
            TempData["Error"] = saveDepositResult.CodeAndMessage();
        }
        else
        {
            TempData["Error"] = getDepositResult.CodeAndMessage();
        }
        return Redirect($"/deposits/{id}");
    }

    public async Task<IActionResult> OnPostSetRightsAndAccess(
        [FromRoute] string id,
        [FromForm] List<string> accessRestrictions,
        [FromForm] Uri? rightsStatement)
    {        
        if (await BindDeposit(id))
        {
            var result = await WorkspaceManager.SetAccessConditions(accessRestrictions, rightsStatement);
            if (result.Success)
            {
                TempData["AccessConditionsUpdated"] = "Access Restrictions and Rights Statement updated.";
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
            }
        }

        return Redirect($"/deposits/{id}");
    }


    public string? GetDisplayTitle()
    {
        if (Deposit == null)
        {
            return Id;
        }

        if (Deposit.ArchivalGroupName.HasText())
        {
            return Deposit.ArchivalGroupName;
        }

        if (Deposit.ArchivalGroup != null)
        {
            return Deposit.ArchivalGroup.GetPathUnderRoot()!;
        }

        return Deposit.Id?.GetSlug();
    }
    
    public async Task<List<ImportJobResult>> GetImportJobResults()
    {
        var fetchResultsResult = await DepositJobResultFetcher.GetImportJobResults(Id, mediator);
        if (fetchResultsResult.Success)
        {
            var importJobResults = fetchResultsResult.Value!;
            return importJobResults;
        }

        TempData["Error"] = fetchResultsResult.CodeAndMessage();
        return [];
    }

    public async Task<List<ProcessPipelineResult>> GetPipelineJobResults()
    {
        var fetchResultsResult = await DepositJobResultFetcher.GetPipelineJobResults(Id, mediator);
        if (fetchResultsResult.Success)
        {
            var pipelineJobResults = fetchResultsResult.Value!;
            return pipelineJobResults;
        }

        TempData["Error"] = fetchResultsResult.CodeAndMessage();
        return [];
    }

    public string GetDepositLocation()
    {
        if (Deposit != null)
        {
            if (Deposit.Files?.Scheme == "s3")
            {
                const string template =
                    "https://eu-west-1.console.aws.amazon.com/s3/buckets/{bucket}?region=eu-west-1&bucketType=general&prefix={prefix}&showversions=false";
                var s3Uri = new AmazonS3Uri(Deposit.Files);
                string href = template
                    .Replace("{bucket}", s3Uri.Bucket)
                    .Replace("{prefix}", s3Uri.Key.TrimEnd('/') + "/");
                return href;
            }
        }

        return "#";
    }


    public async Task<(List<ProcessPipelineResult> jobs, ProcessPipelineResult? runningJob)> GetCleanedPipelineJobsRunning()
    {
        var allJobs = GetPipelineJobResults().Result;
        var oneDayAgo = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(pipelineOptions.Value.PipelineJobsCleanupMinutes));
        var longRunningUnfinishedJobs = allJobs
            .Where(x => x.DateBegun.HasValue && x.DateBegun.Value < oneDayAgo && x.Deposit == Id)
            .Where(x => PipelineJobStates.IsNotComplete(x.Status))
            .ToList();

        var longRunningUnfinishedWaitingJobs = allJobs
            .Where(x => x.Created.HasValue && x.Created.Value < oneDayAgo && x.Deposit == Id)
            .Where(x => PipelineJobStates.IsNotComplete(x.Status))
            .ToList();


        longRunningUnfinishedJobs.AddRange(longRunningUnfinishedWaitingJobs);

        foreach (var job in longRunningUnfinishedJobs)
        {
            var pipelineDeposit = new PipelineDeposit
            {
                Id = job.JobId!,
                Status = PipelineJobStates.CompletedWithErrors,
                DepositId = job.Deposit,
                RunUser = job.RunUser,
                Errors = "Cleaned up as previous processing did not complete"
            };

            var result = await mediator.Send(new ReleaseLock(Deposit!));
            await preservationApiClient.LogPipelineRunStatus(pipelineDeposit, CancellationToken.None);
        }
        
        var latestJob = allJobs
            .Where(x => x.DateBegun.HasValue && x.DateBegun.Value >= oneDayAgo && x.Deposit == Id)
            .OrderByDescending(x => x.DateBegun)
            .FirstOrDefault();

        var latestWaitingJob = allJobs
            .Where(x => x.Created.HasValue && x.Created.Value >= oneDayAgo && x.Deposit == Id)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault();

        if (latestJob == null && latestWaitingJob == null)
        {
            return (allJobs, null);
        }

        ProcessPipelineResult? runningJob = null;
        if (latestJob != null && PipelineJobStates.IsNotComplete(latestJob.Status))
        {
            runningJob = latestJob; 
        }

        if (runningJob == null && latestWaitingJob != null && PipelineJobStates.IsNotComplete(latestWaitingJob.Status))
        {
            runningJob = latestWaitingJob;
        }

        return (allJobs, runningJob);
    }
}

