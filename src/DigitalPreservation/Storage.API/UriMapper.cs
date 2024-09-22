using DigitalPreservation.Core.Utils;

namespace Storage.API;

public class UriMapper
{
    public const string RepositoryPathPrefix = "repository";
    
    public static Uri GetFedoraRelativeUri(string repositoryPath)
    {
        return new Uri(repositoryPath.RemoveStart(RepositoryPathPrefix)!, UriKind.Relative);
    }
}