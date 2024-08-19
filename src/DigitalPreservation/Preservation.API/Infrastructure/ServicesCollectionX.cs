namespace Preservation.API.Infrastructure;

public static class ServicesCollectionX
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