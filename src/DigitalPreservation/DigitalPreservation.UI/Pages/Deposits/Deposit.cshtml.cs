using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.Workspace;
using DigitalPreservation.UI.Workspace;
using DigitalPreservation.Utils;
using LateApexEarlySpeed.Xunit.Assertion.Json;
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
            var result = await WorkspaceManager.CreateFolder(newFolderName, newFolderContext, contextIsFile);
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
            var result = await WorkspaceManager.DeleteItems(deleteSelection, deleteFrom);
            if (result.Success)
            {
                var details = result.Value!;
                TempData["Deleted"] = $"{details.DeletedItems.Count} items DELETED.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = result.ErrorMessage;
        }
        
        return Page();
    }
    
    // I am up to here, still need to convert the next one
    
    
    
    
    public async Task<IActionResult> OnPostUploadFile(
        [FromRoute] string id,
        [FromForm] List<IFormFile> depositFile,
        [FromForm] string checksum,
        [FromForm] string depositFileName,
        [FromForm] string depositFileContentType,
        [FromForm] string? newFileContext,
        [FromForm] bool contextIsFile)
    {
        // This is a PROVISIONAL implementation and will be replaced by an upload widget
        // that can upload multiple, stream large files etc.
        // https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0
        if (await BindDeposit(id))
        {
            if (contextIsFile && newFileContext.HasText())
            {
                newFileContext = newFileContext.GetParent();
            }
            if (depositFile.Count == 0)
            {
                TempData["Error"] = "No file uploaded";
                return Page();
            }
            if (depositFile.Count > 1)
            {
                TempData["Error"] = "More than one file uploaded";
                return Page();
            }
            var s3Root = Deposit!.Files;
            var parentDirectory = Files!.FindDirectory(newFileContext);
            if (parentDirectory == null)
            {
                TempData["Error"] = $"Folder path {newFileContext} could not be found.";
                return Page();
            }
            if (!(parentDirectory.LocalPath == "objects" || parentDirectory.LocalPath.StartsWith("objects/")))
            {
                TempData["Error"] = "Uploaded files must go in or below the objects folder.";
                return Page();
            }

            var slug = PreservedResource.MakeValidSlug(depositFile[0].FileName);
            if (parentDirectory.Directories.Any(d => d.GetSlug() == slug) ||
                parentDirectory.Files.Any(f => f.GetSlug() == slug))
            {
                TempData["Error"] = "This file name conflicts with " + slug;
                return Page();
            }

            var uploadFileResult = await mediator.Send(new UploadFileToDeposit(
                s3Root!,
                newFileContext,
                slug,
                depositFile[0].OpenReadStream(),
                depositFile[0].Length,
                checksum,
                depositFileName,
                depositFileContentType,
                Deposit.MetsETag!));
            if (uploadFileResult.Success)
            {
                TempData["Uploaded"] = "File " + slug + " uploaded.";
                TempData["Context"] = newFileContext;
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = uploadFileResult.CodeAndMessage();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRebuildDepositFileSystem([FromRoute] string id)
    {
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            var readS3Result = await mediator.Send(new GetWorkingDirectory(
                getDepositResult.Value!.Files!, true, true));
            
            if (readS3Result.Value == null)
            {
                TempData["Error"] = "Could not read S3 storage.";
                return Redirect($"/deposits/{id}");
            }
            
            TempData["Valid"] = "View of storage has been updated from S3";
            return Redirect($"/deposits/{id}");
        }

        TempData["Error"] = "Could not GET deposit " + id;
        return Redirect($"/deposits/{id}");
    }

    public async Task<IActionResult> OnPostValidateStorage([FromRoute] string id)
    {
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            var readS3Result = await mediator.Send(new GetWorkingDirectory(
                getDepositResult.Value!.Files!, true, false));
            var readMetsResult = await mediator.Send(new GetWorkingDirectory(
                getDepositResult.Value!.Files!, false, false));

            if (readS3Result.Value == null)
            {
                TempData["Error"] = "Could not read S3 storage.";
                return Redirect($"/deposits/{id}");
            }
            if (readMetsResult.Value == null)
            {
                TempData["Error"] = "Could not read Deposit File System File.";
                return Redirect($"/deposits/{id}");
            }
            var s3Json = JsonSerializer.Serialize(RemoveRootMetadata(readS3Result.Value));
            var metsJson = JsonSerializer.Serialize(RemoveRootMetadata(readMetsResult.Value));
            try
            {
                JsonAssertion.Equivalent(s3Json, metsJson);
                TempData["Valid"] = "Storage validation succeeded. The Deposit File System file reflects S3 content.";
            }
            catch (Exception e)
            {
                TempData["Error"] = "Storage validation Failed. " + e.Message;
            }
            return Redirect($"/deposits/{id}");
        }
        TempData["Error"] = "Could not GET deposit " + id;
        return Redirect($"/deposits/{id}");
    }

    private WorkingDirectory? RemoveRootMetadata(WorkingDirectory wd)
    {
        wd.Modified = DateTime.MinValue;
        // Also do not compare the object directory
        var objects = wd.Directories.SingleOrDefault(d => d.LocalPath == "objects");
        if (objects != null)
        {
            objects.Modified = DateTime.MinValue;
        }
        foreach (var workingFile in wd?.Files ?? [])
        {
            workingFile.Digest = null;
            workingFile.Modified = DateTime.MinValue;
            workingFile.Size = 0;
        }

        return wd;
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
}

