using Microsoft.EntityFrameworkCore;
using Storage.API.Data;
using Testcontainers.PostgreSql;

namespace Storage.API.Tests.TestingInfrastructure;

public class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgresContainer;

    public StorageContext DbContext { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public DatabaseFixture()
    {
        var postgresBuilder = new PostgreSqlBuilder()
            .WithImage("postgres:14-alpine")
            .WithDatabase("db")
            .WithUsername("postgres")
            .WithPassword("postgres_pword")
            .WithCleanUp(true)
            .WithLabel("digitalpreservation_test", "True");

        postgresContainer = postgresBuilder.Build();
    }

    public async Task InitializeAsync()
    {
        // Start DB + apply migrations
        await postgresContainer.StartAsync();
        SetPropertiesFromContainer();
        await DbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => postgresContainer.StopAsync();

    private void SetPropertiesFromContainer()
    {
        ConnectionString = postgresContainer.GetConnectionString();

        // Create new context using connection string for Postgres container
        DbContext = CreateNewStorageContext();
    }

    public StorageContext CreateNewStorageContext()
        => new(
            new DbContextOptionsBuilder<StorageContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options
        );
}

[CollectionDefinition(CollectionName)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string CollectionName = "Storage Database Collection";
}
