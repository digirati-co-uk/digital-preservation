namespace Storage.API.Infrastructure;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add required health checks
    /// </summary>
    public static IServiceCollection AddStorageHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }
}