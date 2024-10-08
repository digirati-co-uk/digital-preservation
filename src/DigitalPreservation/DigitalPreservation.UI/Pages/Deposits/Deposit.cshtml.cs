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
    
    public async Task OnGet([FromRoute] string id)
    {
        await BindDeposit(id);
    }

    private async Task<bool> BindDeposit(string id)
    {
        Id = id;
        var getDepositResult = await mediator.Send(new GetDeposit(id));
        if (getDepositResult.Success)
        {
            Deposit = getDepositResult.Value!;
            var readS3Result = await mediator.Send(new ReadS3(Deposit.Files!));
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
            var slug = PreservedResource.MakeValidSlug(depositFileName);
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

