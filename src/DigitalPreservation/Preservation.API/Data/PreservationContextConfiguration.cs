using Microsoft.EntityFrameworkCore;

namespace Preservation.API.Data;

/// <summary>
/// Helpers for configuring db context
/// </summary>
internal static class PreservationContextConfiguration
{
    private const string ConnectionStringKey = "Postgres";
    private const string RunMigrationsKey = "RunMigrations";

    /// <summary>
    /// Register and configure <see cref="PreservationContext"/> 
    /// </summary>
    public static IServiceCollection AddPreservationContext(this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddDbContext<PreservationContext>(options =>
                SetupOptions(configuration, options));

    /// <summary>
    /// Run EF migrations if "RunMigrations" = true
    /// </summary>
    public static IApplicationBuilder TryRunMigrations(this IApplicationBuilder applicationBuilder,
        IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue(RunMigrationsKey, false)) return applicationBuilder;

        using var context = new PreservationContext(GetOptionsBuilder(configuration).Options);

        var pendingMigrations = context.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count == 0)
        {
            logger.LogInformation("No migrations to run");
            return applicationBuilder;
        }

        logger.LogInformation("Running migrations: {Migrations}", string.Join(",", pendingMigrations));
        context.Database.Migrate();

        return applicationBuilder;
    }

    private static DbContextOptionsBuilder<PreservationContext> GetOptionsBuilder(IConfiguration configuration)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PreservationContext>();
        SetupOptions(configuration, optionsBuilder);
        return optionsBuilder;
    }

    private static void SetupOptions(IConfiguration configuration,
        DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .UseNpgsql(configuration.GetConnectionString(ConnectionStringKey))
            .UseSnakeCaseNamingConvention();
}