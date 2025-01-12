using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.UI.Features.Preservation;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.UI.Features.S3;
using DigitalPreservation.Utils;
using LateApexEarlySpeed.Xunit.Assertion.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Preservation.Client;

namespace DigitalPreservation.UI.Pages.Deposits;

public class DepositModel : PageModel
{
    private readonly IMediator mediator;
    private readonly IOptions<PreservationOptions> options;

    public DepositModel(IMediator mediator, IOptions<PreservationOptions> options)
    {
        this.mediator = mediator;
        this.options = options;
    }

    public bool Bound { get; set; }

    public required string Id { get; set; }
    
    public Deposit? Deposit { get; set; }
    public WorkingDirectory? Files { get; set; }
    
    public List<ImportJobResult> ImportJobResults { get; set; } = [];
    
    public async Task OnGet(
        [FromRoute] string id,
        [FromQuery] bool readFromStorage = false,
        [FromQuery] bool writeToStorage = false)
    {
        Bound = await BindDeposit(id, readFromStorage, writeToStorage);
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

            var fetchResultsResult = await DepositJobResultFetcher.GetImportJobResults(id, mediator);
            if (fetchResultsResult.Success)
            {
                ImportJobResults = fetchResultsResult.Value!;
            }
            else
            {
                TempData["Error"] = fetchResultsResult.CodeAndMessage();
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
                TempData["Context"] = newFolderContext + "/" + slug;
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
                depositFile[0],
                checksum,
                depositFileName,
                depositFileContentType));
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
                TempData["Error"] = "Could not read METSlike file.";
                return Redirect($"/deposits/{id}");
            }
            var s3Json = JsonSerializer.Serialize(RemoveRootMetadata(readS3Result.Value));
            var metsJson = JsonSerializer.Serialize(RemoveRootMetadata(readMetsResult.Value));
            try
            {
                JsonAssertion.Equivalent(s3Json, metsJson);
                TempData["Valid"] = "Storage validation succeeded. The METS file reflects S3 content.";
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

    public bool HasValidFiles()
    {
        var objects = Files?.Directories.SingleOrDefault(d => d.GetSlug() == "objects");
        if (objects == null)
        {
            return false;
        }
        return objects.DescendantFileCount() > 0;
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
}

