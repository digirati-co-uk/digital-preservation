using DigitalPreservation.Common.Model.Results;

namespace Storage.Repository.Common;

public interface IStorage
{
    Task<Result<Uri>> GetWorkingFilesLocation(string idPart, string? callerIdentity = null);
    Task<ConnectivityCheckResult> CanSeeStorage(string source);
}