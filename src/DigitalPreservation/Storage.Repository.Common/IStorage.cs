using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;

namespace Storage.Repository.Common;

public interface IStorage
{
    /// <summary>
    /// Carve out the working files area for a new deposit and return its location. From then on
    /// the deposit's own Files URI is authoritative - every other member of this interface takes
    /// a location, not a root.
    /// </summary>
    /// <param name="idPart">The deposit's minted id.</param>
    /// <param name="templateType">Deposit layout to scaffold.</param>
    /// <param name="workingRoot">
    /// The storage root to carve the area from, or null for the implementation's configured
    /// default. What a root is depends on the implementation: a bucket name for S3, a root
    /// directory for a filesystem implementation.
    /// </param>
    /// <param name="createMetadataFolders">Whether to scaffold the metadata folders.</param>
    Task<Result<Uri>> GetWorkingFilesLocation(string idPart, TemplateType templateType, string? workingRoot = null, bool createMetadataFolders = true);
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
    Task<Result<(Stream? ResponseStream, DateTime LastModified)>> GetStream(Uri binaryOrigin);
    /// <summary>
    /// Returns a stream covering only the requested byte range together with the actual number of
    /// bytes in the response. The caller uses <see cref="RangedStreamResult.RangedContentLength"/>
    /// to build the Content-Range response header when the original <c>to</c> was null (open-ended).
    /// </summary>
    Task<Result<RangedStreamResult?>> GetRangedStream(Uri binaryOrigin, long from, long? to);
}

public record RangedStreamResult(Stream Stream, long RangedContentLength);