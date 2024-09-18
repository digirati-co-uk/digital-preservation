namespace Storage.Repository.Common;

public interface IStorage
{
    Task<ConnectivityCheckResult> CanSeeStorage(string source);
}