using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Storage.API.Data;
using Storage.API.Features.Import.Data;
using ImportJobEntity = Storage.API.Data.Entities.ImportJob;

namespace Storage.API.Tests.Features.Import;

/// <summary>
/// A job whose processor crashed or was killed mid-run never flips Active back to false, which
/// otherwise wedges every future import for its Archival Group behind a 409 Conflict forever
/// (observed against the IIIF Builder e2e fixture Archival Group on dev). GetActiveJobsForArchivalGroup
/// now treats an Active job received more than 30 minutes ago as abandoned: it no longer counts as a
/// conflict, and is flipped back to Active=false so it stops showing up at all.
/// </summary>
public class ImportJobResultStoreStaleJobTests
{
    private static readonly Uri ArchivalGroup = new("https://storage.test/repository/cc/thing");

    private static StorageContext BuildContext()
        => new(new DbContextOptionsBuilder<StorageContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task A_Recently_Received_Active_Job_Still_Counts_As_A_Conflict()
    {
        using var dbContext = BuildContext();
        dbContext.ImportJobs.Add(new ImportJobEntity
        {
            Id = "recent-job",
            ArchivalGroup = ArchivalGroup,
            ImportJobJson = "{}",
            Active = true,
            Received = DateTime.UtcNow.AddMinutes(-5)
        });
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance);

        var result = await sut.GetActiveJobsForArchivalGroup(ArchivalGroup, CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Should().Be("recent-job");
    }

    [Fact]
    public async Task A_Stale_Active_Job_No_Longer_Counts_As_A_Conflict_And_Is_Reaped()
    {
        using var dbContext = BuildContext();
        dbContext.ImportJobs.Add(new ImportJobEntity
        {
            Id = "abandoned-job",
            ArchivalGroup = ArchivalGroup,
            ImportJobJson = "{}",
            Active = true,
            Received = DateTime.UtcNow.AddHours(-2)
        });
        await dbContext.SaveChangesAsync();
        var sut = new ImportJobResultStore(dbContext, NullLogger<ImportJobResultStore>.Instance);

        var result = await sut.GetActiveJobsForArchivalGroup(ArchivalGroup, CancellationToken.None);

        result.Value.Should().BeEmpty("the job is older than the staleness threshold, so it should no longer block new imports");

        var reaped = await dbContext.ImportJobs.SingleAsync(ij => ij.Id == "abandoned-job");
        reaped.Active.Should().BeFalse("reaping should persist, so repeated checks don't keep finding the same stale row");
    }
}
