using System.Net;
using Amazon.S3;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;

namespace Storage.Repository.Common;

public interface IStorage
{
    Task<Result<Uri>> GetWorkingFilesLocation(string idPart, bool useObjectTemplate, string? callerIdentity = null);
    Task<ConnectivityCheckResult> CanSeeStorage(string source);
    
    /// <summary>
    /// Return a representation of the files and folders in an S3 location by reading the METSlike file in its root.
    /// </summary>
    /// <param name="location"></param>
    /// <param name="metsLikeFilename"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> ReadMetsLike(AmazonS3Uri location, string metsLikeFilename, CancellationToken cancellationToken);

    /// <summary>
    /// Return a representation of the files and folders in an S3 location by actually looking at them via S3 APIs.
    /// Generate a tree of WorkingDirectory/WorkingFile, using AWS metadata
    /// This is a potentially expensive operation
    /// Use this to verify a METSlike.json, ot to verify ReadMetsLike
    /// </summary>
    /// <param name="location"></param>
    /// <param name="writeToStorage">Having built the METSlike model - write it to location</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> GenerateMetsLike(AmazonS3Uri location, bool writeToStorage, CancellationToken cancellationToken);
    
    Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory directoryToAdd, CancellationToken cancellationToken);
    Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingFile fileToAdd, CancellationToken cancellationToken);
    Task<Result> DeleteFromMetsLike(AmazonS3Uri location, string metsLikeFilename, string path, CancellationToken cancellationToken);

    Result ResultFailFromS3Exception(AmazonS3Exception s3E, string message, Uri s3Uri);
    Result ResultFailFromAwsStatusCode(HttpStatusCode respHttpStatusCode, string message, Uri s3Uri);
    
    /// <summary>
    /// Remove all the files in this location, and the location itself!
    /// </summary>
    /// <param name="storageLocation"></param>
    /// <returns></returns>
    Task<Result<BulkDeleteResult>> EmptyStorageLocation(Uri storageLocation, CancellationToken cancellationToken);

    static string MetsLike => "__METSlike.json";
}