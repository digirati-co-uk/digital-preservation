using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;

namespace Storage.Repository.Common;

public interface IStorage
{
    Task<Result<Uri>> GetWorkingFilesLocation(string idPart, TemplateType templateType, string? callerIdentity = null);
    Task<ConnectivityCheckResult> CanSeeStorage(string source);
    
    /// <summary>
    /// Return a representation of the files and folders in an S3 location by reading the special deposit file in its root.
    /// This is a JSON document that "caches" the file system layout of the deposit.
    /// This call will never write anything, only read.
    /// </summary>
    /// <param name="location"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> ReadDepositFileSystem(Uri location, CancellationToken cancellationToken);

    /// <summary>
    /// Return a representation of the files and folders in an S3 location by actually looking at them via S3 APIs.
    /// Generate a tree of WorkingDirectory/WorkingFile, using AWS metadata
    /// This is a potentially expensive operation, depending on the size of the Deposit
    /// Use this to verify a DepositFileSystem, or to verify ReadDepositFileSystem
    /// </summary>
    /// <param name="location"></param>
    /// <param name="writeToStorage">Having built the DepositFileSystem model - write it to location</param>
    /// <param name="decorator">A callback that allows additional metadata to be added to the file or folder representation</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> GenerateDepositFileSystem(
        Uri location, bool writeToStorage, Action<WorkingBase>? decorator, CancellationToken cancellationToken);
    
    Task<Result<WorkingDirectory>> AddToDepositFileSystem(Uri location, WorkingDirectory directoryToAdd, CancellationToken cancellationToken);
    Task<Result<WorkingDirectory>> AddToDepositFileSystem(Uri location, WorkingFile fileToAdd, CancellationToken cancellationToken);
    Task<Result> DeleteFromDepositFileSystem(Uri location, string path, bool errorIfNotFound, CancellationToken cancellationToken);

    /// <summary>
    /// Test to see if a file or folder exists in the storage location.
    /// WARNING - this will return false for "folder" path elements of an S3 URI, like
    /// s3://bucket/some/path/in/bucket
    /// In this bucket, some/path/ DOES NOT EXIST
    /// Only explicitly created path/ keys (symbolising an empty folder) will return true.
    /// </summary>
    /// <param name="fileOrFolder">Use a trailing slash to indicate a folder</param>
    /// <returns></returns>
    Task<bool> Exists(Uri fileOrFolder);
    
    /// <summary>
    /// Return a list of files and folders under root, or under root + subpath
    /// </summary>
    /// <param name="root"></param>
    /// <param name="subPath"></param>
    /// <returns></returns>
    Task<List<Uri>> GetListing(Uri root, string? subPath = null);
    
    /// <summary>
    /// Remove all the files in this location, and the location itself!
    /// </summary>
    /// <param name="storageLocation"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<BulkDeleteResult>> EmptyStorageLocation(Uri storageLocation, CancellationToken cancellationToken);
    static string DepositFileSystem => "__METSlike.json";
    Task<Result<string?>> GetExpectedDigest(Uri? binaryOrigin, string? binaryDigest);
    Task<Result<Stream?>> GetStream(Uri binaryOrigin);
}