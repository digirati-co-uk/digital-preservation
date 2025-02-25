using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.UI.Features.Workspace;
using DigitalPreservation.Utils;
using MediatR;
using Storage.Repository.Common;

namespace DigitalPreservation.UI.Workspace;

public class WorkspaceManager(
    Deposit deposit,
    ILogger<WorkspaceManager> logger,
    IMediator mediator,
    IStorage storage,
    IMetsParser metsParser,
    IMetsManager metsManager)
{
    public List<string> Warnings { get; } = [];

    public bool HasValidFiles { get; set; }

    private async Task<WorkingDirectory?> GetFileSystemWorkingDirectory()
    {
        var readFilesResult = await mediator.Send(new GetWorkingDirectory(
            deposit.Files!, false, false, deposit.LastModified));
        if (readFilesResult is { Success: true, Value: not null })
        {
            return readFilesResult.Value;
        }
        Warnings.Add("Could not read working directory: " + readFilesResult.CodeAndMessage());
        return null;
    }
    
    public async Task<CombinedDirectory?> GetCombinedDirectory()
    {
        var metsWrapper = await GetMetsWrapper(); // what if there is no METS?
        var fileSystem = await GetFileSystemWorkingDirectory();
        var combined = CombinedBuilder.Build(fileSystem, metsWrapper?.PhysicalStructure);
        var objects = combined.Directories.SingleOrDefault(d => d.LocalPath == "objects");
        if (objects == null || objects.DescendantFileCount() == 0)
        {
            HasValidFiles = false;
        }
        else
        {
            HasValidFiles = true;
        }
        return combined;
    }
    
        
    private async Task<MetsFileWrapper?> GetMetsWrapper()
    {
        var result = await metsParser.GetMetsFileWrapper(deposit.Files!, true);
        if (result is { Success: true, Value: not null })
        {
            return result.Value;
        }
        Warnings.Add("Could not obtain METS file wrapper");
        return null;
    }


    public async Task<Result<CreateFolderResult>> CreateFolder(string newFolderName, string? newFolderContext, bool contextIsFile)
    {
        if (contextIsFile && newFolderContext.HasText())
        {
            newFolderContext = newFolderContext.GetParent();
        }
        var combined = await GetCombinedDirectory();
        var parentDirectory = combined!.FindDirectory(newFolderContext);
        if (parentDirectory == null)
        {
            return Result.FailNotNull<CreateFolderResult>(ErrorCodes.NotFound,
                $"Folder path {newFolderContext} could not be found.");
        }
        var slug = PreservedResource.MakeValidSlug(newFolderName);
        if (parentDirectory.Directories.Any(d => d.LocalPath!.GetSlug() == slug) ||
            parentDirectory.Files.Any(f => f.LocalPath!.GetSlug() == slug))
        {
            // TODO: As long as the name doesn't conflict, we should CREATE url-safe aliases (e.g., filename-1)
            
            return Result.FailNotNull<CreateFolderResult>(ErrorCodes.Conflict,
                "This directory name conflicts with " + slug);
        }

        var createFolderResult = await mediator.Send(new CreateFolder(
            deposit.Files!, newFolderName, slug, newFolderContext, deposit.MetsETag!));
        if (createFolderResult.Success)
        {
            var result = new CreateFolderResult
            {
                Created = "Folder " + slug + " created.",
                Context = newFolderContext + "/" + slug
            };
            return Result.OkNotNull(result); 
        }
        
        return Result.FailNotNull<CreateFolderResult>(
            createFolderResult.ErrorCode ?? ErrorCodes.UnknownError, createFolderResult.ErrorMessage);

    }

    public async Task<Result<DeleteItemsResult>> DeleteItems(DeleteSelection deleteSelection, Whereabouts? deleteFrom)
    {
        if (deleteFrom is null or Whereabouts.Mets or Whereabouts.Neither)
        {
            return Result.FailNotNull<DeleteItemsResult>(ErrorCodes.BadRequest,
                "No location to delete from specified.");
        }

        if (deleteSelection.Items.Count == 0)
        {
            return Result.FailNotNull<DeleteItemsResult>(ErrorCodes.BadRequest,
                "No items to delete.");
        }
        
        deleteSelection.DeleteFromDepositFiles = deleteFrom is Whereabouts.Deposit or Whereabouts.Both;
        deleteSelection.DeleteFromMets = deleteFrom is Whereabouts.Both;
        deleteSelection.Deposit = deposit.Id;

        var goodResult = new DeleteItemsResult();

        // This loop is editing the METS one at a time, needs to be more sensible
        foreach (var item in deleteSelection.Items)
        {
            var deleteDirectoryContext = item.RelativePath;
            if (!item.IsDirectory)
            {
                deleteDirectoryContext = deleteDirectoryContext.GetParent();
            }

            var combined = await GetCombinedDirectory();
            var deleteDirectory = combined!.FindDirectory(deleteDirectoryContext);
            if (deleteDirectory == null)
            {
                return Result.FailNotNull<DeleteItemsResult>(
                    ErrorCodes.NotFound, $"Directory {deleteDirectoryContext} not found.");
            }

            if (item.IsDirectory)
            {
                if (deleteDirectory.LocalPath == "objects")
                {
                    return Result.FailNotNull<DeleteItemsResult>(
                        ErrorCodes.BadRequest, "You cannot delete the objects directory.");
                }

                if (deleteDirectory.Files.Count > 0)
                {
                    return Result.FailNotNull<DeleteItemsResult>(
                        ErrorCodes.BadRequest, "You cannot delete a folder that has files in it; delete the files first.");
                }

                var deleteDirectoryResult = await mediator.Send(new DeleteObject(deposit.Files!,
                    deleteDirectory.LocalPath!, deposit.MetsETag!,
                    deleteSelection.DeleteFromDepositFiles, deleteSelection.DeleteFromMets));
                if (deleteDirectoryResult.Success)
                {
                    goodResult.DeletedItems.Add(item);
                }
                else
                {
                    return Result.FailNotNull<DeleteItemsResult>(
                        deleteDirectoryResult.ErrorCode!,
                        $"DeleteItems failed after {goodResult.DeletedItems.Count} with: {deleteDirectoryResult.ErrorMessage}.");
                }
            }
            else
            {
                var fileToDelete = deleteDirectory.Files.SingleOrDefault(f => f.LocalPath == item.RelativePath);
                if (fileToDelete == null)
                {
                    return Result.FailNotNull<DeleteItemsResult>(
                        ErrorCodes.NotFound, $"File {item.RelativePath} not found.");
                }

                if (!fileToDelete.LocalPath!.Contains('/'))
                {
                    return Result.FailNotNull<DeleteItemsResult>(
                        ErrorCodes.BadRequest, "You cannot delete files in the root.");
                }

                var deleteFileResult = await mediator.Send(
                    new DeleteObject(
                        deposit.Files!, fileToDelete.LocalPath, deposit.MetsETag!,
                        deleteSelection.DeleteFromDepositFiles, deleteSelection.DeleteFromMets));
                if (deleteFileResult.Success)
                {
                    goodResult.DeletedItems.Add(item);
                }
                else
                {
                    return Result.FailNotNull<DeleteItemsResult>(
                        deleteFileResult.ErrorCode!,
                        $"DeleteItems failed after {goodResult.DeletedItems.Count} with: {deleteFileResult.ErrorMessage}.");
                }
                
            }
        }

        return Result.FailNotNull<DeleteItemsResult>(ErrorCodes.Unprocessable, "NOT FINISHED YET");

        
        
        // Interim
        // var deleteResult = await mediator.Send(new DeleteItems(Deposit.Files, deleteSelection));
        // if (deleteResult.Success)
        // {
        //     TempData["Deleted"] = $"{deleteSelection.Items.Count} items DELETED.";
        //     return Redirect($"/deposits/{id}");
        // }
        //
        // TempData["Error"] = deleteResult.CodeAndMessage();
        // return Redirect($"/deposits/{id}");


    }
}