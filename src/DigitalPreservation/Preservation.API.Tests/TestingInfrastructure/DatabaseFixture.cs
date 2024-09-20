using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Testcontainers.PostgreSql;

namespace Preservation.API.Tests.TestingInfrastructure;

public class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer  postgresContainer;

    public PreservationContext DbContext { get; private set; } = null!;
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
        try
        {
            await postgresContainer.StartAsync();
            SetPropertiesFromContainer();
            await DbContext.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            var m = ex.Message;
            throw;
        }
    }

    public Task DisposeAsync() => postgresContainer.StopAsync();
    
    private void SetPropertiesFromContainer()
    {
        ConnectionString = postgresContainer.GetConnectionString();

        // Create new context using connection string for Postgres container
        DbContext = CreateNewAuthServiceContext();
    }
    
    public PreservationContext CreateNewAuthServiceContext()
        => new(
            new DbContextOptionsBuilder<PreservationContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options
        );
}

[CollectionDefinition(CollectionName)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string CollectionName = "Database Collection";
}