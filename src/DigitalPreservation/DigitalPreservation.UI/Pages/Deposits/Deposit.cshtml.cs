using System.Text.Json;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Import;
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

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel(
    IMediator mediator, 
    IOptions<PreservationOptions> options,
    WorkspaceManagerFactory workspaceManagerFactory) : PageModel
{
    public required string Id { get; set; }
    public required WorkspaceManager WorkspaceManager { get; set; }
    public Deposit? Deposit { get; set; }
    public string? ArchivalGroupTestWarning { get; set; }

    public bool ArchivalGroupExists => Deposit is not null && Deposit.ArchivalGroupExists;

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
            WorkspaceManager = workspaceManagerFactory.Create(Deposit);
            
            if (!Deposit.ArchivalGroupExists && Deposit.ArchivalGroup != null && Deposit.ArchivalGroup.GetPathUnderRoot().HasText())
            {
                var testArchivalGroupResult = await mediator.Send(new TestArchivalGroupPath(Deposit.ArchivalGroup.GetPathUnderRoot()!));
                if (testArchivalGroupResult.Failure)
                {
                    ArchivalGroupTestWarning = testArchivalGroupResult.ErrorMessage;
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
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }
        
        return Page();
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
            var combinedResult = await WorkspaceManager.GetCombinedDirectory(true);
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
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }

        return Page();

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
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostValidateStorage([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await WorkspaceManager.ValidateDepositFileSystem();
            if (result.Success)
            {
                TempData["Valid"] = "Storage validation succeeded. The Deposit File System file reflects S3 content.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostReleaseLock([FromRoute] string id)
    {
        if (await BindDeposit(id))
        {
            var result = await mediator.Send(new ReleaseLock(Deposit!));
            if (result.Success)
            {
                TempData["Valid"] = "Lock released.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }

        return Page();
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
}

