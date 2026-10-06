using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;

namespace Preservation.API.Tests.Features.Activity;

/// <summary>
/// GetLatestArchivalGroupEvent must return a row even when the seeded backstop event
/// (PreservationContext.OnModelCreating) is the only one in the table - that is its entire reason
/// to exist, and it is what breaks if someone "fixes" issue #269 by deleting the seed instead of
/// suppressing it. An EF Core in-memory database (rather than the shared Postgres
/// Testcontainers fixture the rest of this feature's tests use) is what gives this test a table
/// it can be sure holds nothing else.
/// </summary>
public class SeedWatermarkTests
{
    [Fact]
    public async Task The_Seed_Row_Is_The_Watermark_When_It_Is_The_Only_Event()
    {
        var options = new DbContextOptionsBuilder<PreservationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PreservationContext(options);
        await context.Database.EnsureCreatedAsync();

        var latest = context.GetLatestArchivalGroupEvent();

        latest.Should().NotBeNull("the watermark must never be absent, even on an empty table");
        latest!.Id.Should().Be(-1);
    }
}
