using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Tests.TestingInfrastructure;

namespace Preservation.API.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(DatabaseCollection.CollectionName)]
public class DatabaseTest
{
    private readonly PreservationContext dbContext;

    public DatabaseTest(DatabaseFixture dbFixture)
    {
        dbContext = dbFixture.DbContext;
    }

    [Fact]
    public async Task ConfirmSetup()
    {
        // NOTE - this is a fairly pointless test, only here to confirm plumbing in test
        var pending = await dbContext.Database.GetPendingMigrationsAsync();
        pending.Should().BeNullOrEmpty();
    }
}