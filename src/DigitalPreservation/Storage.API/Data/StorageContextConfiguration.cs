using Microsoft.EntityFrameworkCore;

namespace Storage.API.Data;

public static class StorageContextConfiguration
{
    private const string ConnectionStringKey = "Postgres";
    private const string RunMigrationsKey = "RunMigrations";

    /// <summary>
    /// Register and configure <see cref="StorageContext"/> 
    /// </summary>
    public static IServiceCollection AddStorageContext(this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddDbContext<StorageContext>(options =>
                SetupOptions(configuration, options));

    /// <summary>
    /// Run EF migrations if "RunMigrations" = true
    /// </summary>
    public static IApplicationBuilder TryRunMigrations(this IApplicationBuilder applicationBuilder,
        IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue(RunMigrationsKey, false)) return applicationBuilder;

        using var context = new StorageContext(GetOptionsBuilder(configuration).Options);

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

    private static DbContextOptionsBuilder<StorageContext> GetOptionsBuilder(IConfiguration configuration)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StorageContext>();
        SetupOptions(configuration, optionsBuilder);
        return optionsBuilder;
    }

    private static void SetupOptions(IConfiguration configuration,
        DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .UseNpgsql(configuration.GetConnectionString(ConnectionStringKey))
            .UseSnakeCaseNamingConvention();
}