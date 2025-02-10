using Amazon.S3.Util;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;

namespace Storage.Repository.Common;

public interface IStorage
{
    Task<Result<Uri>> GetWorkingFilesLocation(string idPart, bool useObjectTemplate, string? callerIdentity = null);
    Task<ConnectivityCheckResult> CanSeeStorage(string source);
    
    /// <summary>
    /// Return a representation of the files and folders in an S3 location by reading the special deposit file in its root.
    /// </summary>
    /// <param name="location"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> ReadDepositFileSystem(AmazonS3Uri location, CancellationToken cancellationToken);

    /// <summary>
    /// Return a representation of the files and folders in an S3 location by actually looking at them via S3 APIs.
    /// Generate a tree of WorkingDirectory/WorkingFile, using AWS metadata
    /// This is a potentially expensive operation
    /// Use this to verify a DepositFileSystem, ot to verify ReadDepositFileSystem
    /// </summary>
    /// <param name="location"></param>
    /// <param name="writeToStorage">Having built the DepositFileSystem model - write it to location</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> GenerateDepositFileSystem(AmazonS3Uri location, bool writeToStorage, CancellationToken cancellationToken);
    
    Task<Result<WorkingDirectory>> AddToDepositFileSystem(AmazonS3Uri location, WorkingDirectory directoryToAdd, CancellationToken cancellationToken);
    Task<Result<WorkingDirectory>> AddToDepositFileSystem(AmazonS3Uri location, WorkingFile fileToAdd, CancellationToken cancellationToken);
    Task<Result> DeleteFromDepositFileSystem(AmazonS3Uri location, string path, CancellationToken cancellationToken);

    /// <summary>
    /// Remove all the files in this location, and the location itself!
    /// </summary>
    /// <param name="storageLocation"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<BulkDeleteResult>> EmptyStorageLocation(Uri storageLocation, CancellationToken cancellationToken);
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="sourceUri"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<ImportSource>> GetImportSource(Uri sourceUri, CancellationToken cancellationToken);

    static string DepositFileSystem => "__METSlike.json";
    Task<Result<string?>> GetExpectedDigest(Uri? binaryOrigin, string? binaryDigest);
    Task<byte[]> GetBytes(Uri binaryOrigin);
}