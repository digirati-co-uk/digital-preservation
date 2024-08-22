namespace Preservation.API.Infrastructure;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add required health checks
    /// </summary>
    public static IServiceCollection AddPreservationHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }
}