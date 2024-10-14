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
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Result<WorkingDirectory?>> GenerateMetsLike(AmazonS3Uri location, CancellationToken cancellationToken);
    
    Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory directoryToAdd, CancellationToken cancellationToken);
    Task<Result<WorkingDirectory>> AddToMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingFile fileToAdd, CancellationToken cancellationToken);
    Task<Result<WorkingDirectory>> DeleteFromMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingDirectory directoryToDelete, CancellationToken cancellationToken);
    Task<Result<WorkingDirectory>> DeleteFromMetsLike(AmazonS3Uri location, string metsLikeFilename, WorkingFile fileToDelete, CancellationToken cancellationToken);


    static string MetsLike => "__METSlike.json";
}