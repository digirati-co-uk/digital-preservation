using System.Text.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using DigitalPreservation.Workspace.Requests;
using LateApexEarlySpeed.Xunit.Assertion.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common;

namespace DigitalPreservation.Workspace;

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
    public string? MetsPath { get; set; }
    public bool Editable { get; set; }
    public string? MetsName { get; set; }

    public async Task<WorkingDirectory?> GetFileSystemWorkingDirectory(bool refresh = false)
    {
        var readFilesResult = await mediator.Send(new GetWorkingDirectory(
            deposit.Files!, refresh, refresh, deposit.LastModified));
        if (readFilesResult is { Success: true, Value: not null })
        {
            return readFilesResult.Value;
        }
        Warnings.Add("Could not read working directory: " + readFilesResult.CodeAndMessage());
        return null;
    }
    
    public async Task<CombinedDirectory?> GetCombinedDirectory(bool refresh = false)
    {
        var metsWrapper = await GetMetsWrapper(); // what if there is no METS?
        var fileSystem = await GetFileSystemWorkingDirectory(refresh);
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
            var metsWrapper = result.Value;
            MetsPath = metsWrapper.Self?.LocalPath ?? MetsPath;
            Editable = metsWrapper.Editable;
            MetsName = metsWrapper.Name;
            return metsWrapper;
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

    public async Task<Result<ItemsAffected>> DeleteItems(DeleteSelection deleteSelection, Whereabouts? deleteFrom)
    {
        if (deleteFrom is null or Whereabouts.Mets or Whereabouts.Neither)
        {
            return Result.FailNotNull<ItemsAffected>(ErrorCodes.BadRequest,
                "No location to delete from specified.");
        }

        if (deleteSelection.Items.Count == 0)
        {
            return Result.FailNotNull<ItemsAffected>(ErrorCodes.BadRequest,
                "No items to delete.");
        }
        
        deleteSelection.DeleteFromDepositFiles = deleteFrom is Whereabouts.Deposit or Whereabouts.Both;
        deleteSelection.DeleteFromMets = deleteFrom is Whereabouts.Both;
        deleteSelection.Deposit = deposit.Id;
        
        var combined = await GetCombinedDirectory(true);
        var deleteResult = await mediator.Send(new DeleteItems(deposit.Files, deleteSelection, combined!, deposit.MetsETag!));
        // refresh the file system again
        // need to see how long this operation takes on large deposits
        await GetFileSystemWorkingDirectory(true);
        return deleteResult;
    }

    public async Task<Result<ItemsAffected>> AddItemsToMets(List<WorkingBase> items)
    {
        if (items.Count == 0)
        {
            return Result.FailNotNull<ItemsAffected>(ErrorCodes.BadRequest,
                "No items to add to METS.");
        }
        
        var addToMetsResult = await mediator.Send(new AddItemsToMets(deposit.Files!, items, deposit.MetsETag!));
        return addToMetsResult;
    }

    public async Task<Result<SingleFileUploadResult>> UploadSingleSmallFile(
        Stream stream, long size, string sourceFileName, string checksum, string fileName, string contentType, string? context)
    {

        var combined = await GetCombinedDirectory();
        var parentDirectory = combined!.FindDirectory(context);
        if (parentDirectory == null)
        {
            return Result.FailNotNull<SingleFileUploadResult>(
                ErrorCodes.BadRequest, $"Folder path {parentDirectory} could not be found.");
        }

        if (!(parentDirectory.LocalPath == "objects" || parentDirectory.LocalPath!.StartsWith("objects/")))
        {
            return Result.FailNotNull<SingleFileUploadResult>(
                ErrorCodes.BadRequest, "Uploaded files must go in or below the objects folder.");
        }

        var slug = PreservedResource.MakeValidSlug(sourceFileName);
        if (parentDirectory.Directories.Any(d => d.LocalPath!.GetSlug() == slug) ||
            parentDirectory.Files.Any(f => f.LocalPath!.GetSlug() == slug))
        {
            return Result.FailNotNull<SingleFileUploadResult>(
                ErrorCodes.BadRequest, "This file name conflicts with " + slug);
        }

        var uploadFileResult = await mediator.Send(new UploadFileToDeposit(
            deposit.Files!,
            context,
            slug,
            stream,
            size,
            checksum,
            fileName,
            contentType,
            deposit.MetsETag!));
        if (uploadFileResult.Success)
        {
            var result = new SingleFileUploadResult
            {
                Uploaded = "File " + slug + " uploaded.",
                Context = context
            };
            return Result.OkNotNull(result);
        }

        return Result.FailNotNull<SingleFileUploadResult>(
            uploadFileResult.ErrorCode ?? ErrorCodes.UnknownError, uploadFileResult.ErrorMessage);
    }

    public async Task<Result> RebuildDepositFileSystem()
    {
        var readS3Result = await mediator.Send(new GetWorkingDirectory(
                deposit.Files!, true, true));
        return readS3Result;
    }

    public async Task<Result> ValidateDepositFileSystem()
    {       
        var readS3Result = await mediator.Send(new GetWorkingDirectory(
            deposit.Files!, true, false));
        var readJsonResult = await mediator.Send(new GetWorkingDirectory(
            deposit.Files!, false, false));

        if (readS3Result.Value == null)
        {
            return readS3Result;
        }
        if (readJsonResult.Value == null)
        {
            return readJsonResult;
        }
        var s3Json = JsonSerializer.Serialize(RemoveRootMetadata(readS3Result.Value));
        var metsJson = JsonSerializer.Serialize(RemoveRootMetadata(readJsonResult.Value));
        try
        {
            JsonAssertion.Equivalent(s3Json, metsJson);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.Conflict, "Storage validation Failed. " + e.Message);
        }
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
}
