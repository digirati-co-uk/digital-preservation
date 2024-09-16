namespace Storage.Repository.Common;

public interface IStorage
{
    Task<bool> CanSeeStorage();
}