using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.S3;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel(IMediator mediator) : PageModel
{
    public required string Id { get; set; }
    
    public Deposit? Deposit { get; set; }
    public WorkingDirectory? Files { get; set; }
    
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
            // There is a METSlike for the deposit contents, AND a METS for the AG (if exists).
            // The metslike for deposit contents does not get saved to Fedora (but does it get saved to the DB)
            var readS3Result = await mediator.Send(
                new GetWorkingDirectory(Deposit.Files!, readFromStorage, writeToStorage)); 
            if (readS3Result.Success)
            {
                Files = readS3Result.Value!;
            }
            else
            {
                TempData["Error"] = readS3Result.CodeAndMessage();
                return false;
            }
        }
        else
        {
            TempData["Error"] = getDepositResult.CodeAndMessage();
            return false;
        }

        return true;
    }

    public async Task<IActionResult> OnPostDeleteItem(
        [FromRoute] string id,
        [FromForm] string deleteContext,
        [FromForm] bool deleteContextIsFile)
    {
        if (await BindDeposit(id))
        {
            var deleteDirectoryContext = deleteContext;
            if (deleteContextIsFile)
            {
                deleteDirectoryContext = deleteDirectoryContext.GetParent();
            }
            var deleteDirectory = Files!.FindDirectory(deleteDirectoryContext);
            if (deleteDirectory == null)
            {
                TempData["Error"] = $"Directory {deleteDirectoryContext} not found.";
                return Redirect($"/deposits/{id}");
            }
            if (deleteContextIsFile)
            {
                var fileToDelete = deleteDirectory.Files.SingleOrDefault(f => f.LocalPath == deleteContext);
                if (fileToDelete == null)
                {
                    TempData["Error"] = $"File {deleteContext} not found.";
                    return Redirect($"/deposits/{id}");
                }
                if(!fileToDelete.LocalPath.Contains('/'))
                {
                    TempData["Error"] = "You cannot delete files in the root.";
                    return Redirect($"/deposits/{id}");
                }
                var deleteFileResult = await mediator.Send(new DeleteObject(Deposit!.Files!, fileToDelete.LocalPath));
                if (deleteFileResult.Success)
                {
                    TempData["Deleted"] = "File " + fileToDelete.LocalPath + " DELETED.";
                }
                else
                {
                    TempData["Error"] = deleteFileResult.CodeAndMessage();
                }
                return Redirect($"/deposits/{id}");
            }
            // want to delete a directory
            if(deleteDirectory.LocalPath == "objects")
            {
                TempData["Error"] = "You cannot delete the objects directory.";
                return Redirect($"/deposits/{id}");
            }
            if (deleteDirectory.Files.Count > 0)
            {
                TempData["Error"] = "You cannot delete a folder that has files in it; delete the files first.";
                return Redirect($"/deposits/{id}");
            }
            var deleteDirectoryResult = await mediator.Send(new DeleteObject(Deposit!.Files!, deleteDirectory.LocalPath));
            if (deleteDirectoryResult.Success)
            {
                TempData["Deleted"] = "Folder " + deleteDirectory.LocalPath + " DELETED.";
            }
            else
            {
                TempData["Error"] = deleteDirectoryResult.CodeAndMessage();
            }
            return Redirect($"/deposits/{id}");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateFolder(
        [FromRoute] string id, 
        [FromForm] string newFolderName,
        [FromForm] string? newFolderContext,
        [FromForm] bool contextIsFile)
    {
        if (await BindDeposit(id))
        {
            var s3Root = Deposit!.Files;
            if (contextIsFile && newFolderContext.HasText())
            {
                newFolderContext = newFolderContext.GetParent();
            }
            var parentDirectory = Files!.FindDirectory(newFolderContext);
            if (parentDirectory == null)
            {
                TempData["Error"] = $"Folder path {newFolderContext} could not be found.";
                return Page();
            }
            var slug = PreservedResource.MakeValidSlug(newFolderName);
            if (parentDirectory.Directories.Any(d => d.GetSlug() == slug) ||
                parentDirectory.Files.Any(f => f.GetSlug() == slug))
            {
                // TODO: As long as the name doesn't conflict, we should CREATE url-safe aliases (e.g., filename-1)
                TempData["Error"] = "This directory name conflicts with " + slug;
                return Page();
            }

            var createFolderResult = await mediator.Send(new CreateFolder(s3Root!, newFolderName, slug, newFolderContext));
            if (createFolderResult.Success)
            {
                TempData["Created"] = "Folder " + slug + " created.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = createFolderResult.CodeAndMessage();
        }
        return Page();
    }
    
    public async Task<IActionResult> OnPostUploadFile(
        [FromRoute] string id,
        [FromForm] List<IFormFile> depositFile,
        [FromForm] string checksum,
        [FromForm] string depositFileName,
        [FromForm] string contentType,
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
                depositFile[0],
                checksum,
                depositFileName,
                contentType));
            if (uploadFileResult.Success)
            {
                TempData["Uploaded"] = "File " + slug + " uploaded.";
                return Redirect($"/deposits/{id}");
            }

            TempData["Error"] = uploadFileResult.CodeAndMessage();
        }

        return Page();
    }
}

