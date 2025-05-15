using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using MediatR;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

namespace DigitalPreservation.Workspace.Requests;

public class DeleteItems(
    bool isBagItLayout,
    Uri depositFiles, 
    DeleteSelection deleteSelection, 
    CombinedDirectory combinedRootDirectory,
    string depositETag) : IRequest<Result<ItemsAffected>>
{
    public bool IsBagItLayout { get; } = isBagItLayout;
    public CombinedDirectory CombinedRootDirectory { get; } = combinedRootDirectory;
    public Uri DepositFiles { get; } = depositFiles;
    public DeleteSelection DeleteSelection { get; } = deleteSelection;
    public string DepositETag { get; } = depositETag;
}

public class DeleteItemsHandler(
    IAmazonS3 s3Client,
    IMetsManager metsManager) : IRequestHandler<DeleteItems, Result<ItemsAffected>>
{
    public async Task<Result<ItemsAffected>> Handle(DeleteItems request, CancellationToken cancellationToken)
    { 
        var goodResult = new ItemsAffected();
        var s3Uri = new AmazonS3Uri(request.DepositFiles);
        FullMets? mets = null;
        bool metsHasBeenWrittenTo = false;
        if (request.DeleteSelection.DeleteFromMets)
        {
            var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
            if (metsResult is { Success: true, Value: not null })
            {
                mets = metsResult.Value;
            }
            else
            {
                return Result.FailNotNull<ItemsAffected>(
                    metsResult.ErrorCode ?? ErrorCodes.UnknownError, metsResult.ErrorMessage);
            }
        }
        
        var deepestFirst = request.DeleteSelection.Items
            .OrderByDescending(item => item.RelativePath.Count(c => c == '/'));
        foreach (var item in deepestFirst)
        {
            Result<ItemsAffected>? failedDeleteResult = null;
            bool deletedFromDepositFiles = false;
            var deleteDirectoryContext = item.RelativePath; 
            if (!item.IsDirectory)
            {
                deleteDirectoryContext = deleteDirectoryContext.GetParent();
            }

            CombinedFile? fileToDelete;
            var deleteDirectory = request.CombinedRootDirectory.FindDirectory(deleteDirectoryContext);
            if (deleteDirectory == null)
            {
                failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                    ErrorCodes.NotFound, $"Directory {deleteDirectoryContext} not found.");
            }

            if (deleteDirectory != null)
            {
                if (item.IsDirectory)
                {
                    if (deleteDirectory.LocalPath == FolderNames.Objects)
                    {
                        failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                            ErrorCodes.BadRequest, "You cannot delete the objects directory.");
                    }

                    if (deleteDirectory.Files.Count > 0)
                    {
                        failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                            ErrorCodes.BadRequest,
                            "You cannot delete a folder that has files in it; delete the files first.");
                    }
                }
                else
                {
                    fileToDelete = deleteDirectory.Files.SingleOrDefault(f => f.LocalPath == item.RelativePath);
                    if (fileToDelete == null)
                    {
                        failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                            ErrorCodes.NotFound, $"File {item.RelativePath} not found.");
                    }

                    if (fileToDelete != null && !fileToDelete.LocalPath!.Contains('/'))
                    {
                        failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                            ErrorCodes.BadRequest, "You cannot delete files in the root.");
                    }
                }

                // this is the DeleteObject code
                var depositPath = FolderNames.GetPathPrefix(request.IsBagItLayout) + item.RelativePath;
                if (failedDeleteResult == null)
                {
                    var dor = new DeleteObjectRequest
                    {
                        BucketName = s3Uri.Bucket,
                        Key = s3Uri.Key + depositPath 
                    };
                    if (item.IsDirectory && !dor.Key.EndsWith('/'))
                    {
                        dor.Key += "/";
                    }

                    try
                    {
                        if (request.DeleteSelection.DeleteFromDepositFiles)
                        {
                            // attempt to remove from JSON
                            var deleted = item.IsDirectory ? 
                                request.CombinedRootDirectory.RemoveDirectoryFromDeposit(item.RelativePath, depositPath, true) : 
                                request.CombinedRootDirectory.RemoveFileFromDeposit(item.RelativePath, depositPath, true);
                            if (deleted)
                            {
                                // if we can remove from JSON, remove from S3
                                var response = await s3Client.DeleteObjectAsync(dor, cancellationToken);
                                if (response.HttpStatusCode == HttpStatusCode.NoContent)
                                {
                                    // Here we remove the item from the deposit-files path of the combined filesystem
                                    // If there is no METS counterpart, we delete the combined resource as well.
                                    deletedFromDepositFiles = true;
                                }
                                else
                                {
                                    failedDeleteResult =
                                        ResultHelpers.FailNotNullFromAwsStatusCode<ItemsAffected>(
                                            response.HttpStatusCode, "Could not delete object from S3.",
                                            dor.GetS3Uri());
                                }
                            }
                            else
                            {
                                failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                                    ErrorCodes.UnknownError,
                                    "Could not delete " + item.RelativePath + " from DepositFileSystem JSON");
                            }
                            

                            if (deletedFromDepositFiles && request.DeleteSelection.DeleteFromMets && mets != null)
                            {
                                var deleteFromMetsResult = metsManager.DeleteFromMets(mets, item.RelativePath);
                                if (deleteFromMetsResult.Success)
                                {
                                    metsHasBeenWrittenTo = true;
                                    // Also remove from the METS branch of the combinedDirectory object graph
                                    if (item.IsDirectory)
                                    {
                                        request.CombinedRootDirectory.RemoveDirectoryFromMets(item.RelativePath, depositPath, true);
                                    }
                                    else
                                    {
                                        request.CombinedRootDirectory.RemoveFileFromMets(item.RelativePath, depositPath, true);
                                    }
                                }
                                else
                                {
                                    failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                                        deleteFromMetsResult.ErrorMessage ?? ErrorCodes.UnknownError,
                                        deleteFromMetsResult.ErrorMessage);
                                }
                            }
                        }
                    }
                    catch (AmazonS3Exception s3E)
                    {
                        failedDeleteResult =
                            ResultHelpers.FailNotNullFromS3Exception<ItemsAffected>(
                                s3E, "Could not delete object", dor.GetS3Uri());
                    }
                    catch (Exception e)
                    {
                        failedDeleteResult = Result.FailNotNull<ItemsAffected>(
                            ErrorCodes.UnknownError, "Could not delete object: " + e.Message);
                    }
                }
            }

            if (failedDeleteResult == null)
            {
                goodResult.Items.Add(item);
            }
            else
            {
                return Result.FailNotNull<ItemsAffected>(
                    failedDeleteResult.ErrorCode!,
                    $"Delete failed after {goodResult.Items.Count} items. {failedDeleteResult.ErrorMessage}.");
            }

        }

        if (mets != null && metsHasBeenWrittenTo)
        {
            var writeMetsResult = await metsManager.WriteMets(mets);
            if (writeMetsResult.Failure)
            {
                return Result.FailNotNull<ItemsAffected>(
                    writeMetsResult.ErrorCode!,
                    $"Delete failed after {goodResult.Items.Count} items. Unable to write METS file.");
                
            }
        }
        return Result.OkNotNull(goodResult);
    }
}