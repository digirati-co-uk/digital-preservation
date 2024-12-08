namespace Preservation.API.Mutation;

public static class ServiceCollectionX
{
    public static IServiceCollection AddResourceMutator(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection.Configure<MutatorOptions>(configuration.GetSection(MutatorOptions.ResourceMutator));
        serviceCollection.AddSingleton<ResourceMutator>();
        return serviceCollection;
    }
}