namespace DigitalPreservation.UI.Infrastructure;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add required health checks
    /// </summary>
    public static IServiceCollection AddUIHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }
}