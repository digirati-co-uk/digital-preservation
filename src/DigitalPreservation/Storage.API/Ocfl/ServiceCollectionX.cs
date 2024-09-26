using Storage.API.Fedora;

namespace Storage.API.Ocfl;

public static class ServiceCollectionX
{
    public static IServiceCollection AddOcfl(this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        // Might need to grab settings in here too, later
        serviceCollection.AddSingleton<IStorageMapper, OcflS3StorageMapper>();
        return serviceCollection;
    }
}