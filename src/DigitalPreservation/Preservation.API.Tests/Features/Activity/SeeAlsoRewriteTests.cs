using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Preservation.API.Features.Activity.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;

namespace Preservation.API.Tests.Features.Activity;

/// <summary>
/// The Activity Stream's seeAlso used to name the ImportJobResult with the raw Storage API URI
/// StorageImportJobsProcessor recorded on the event - a URI no Preservation API caller can resolve
/// (issue #265). It must instead be looked up against the ImportJobs table and rewritten to the
/// Preservation API route that actually serves it, and omitted entirely rather than falling back to
/// the Storage URI when no matching row exists.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class SeeAlsoRewriteTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task An_Event_With_A_Recorded_Import_Job_Gets_A_Preservation_Api_SeeAlso()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var storageResultId = new Uri($"https://storage.test/importjobs/{Guid.NewGuid():N}");
        const string depositId = "seealso-dep";
        const string importJobId = "seealso-job";

        context.ImportJobs.Add(SeedImportJob(depositId, importJobId, storageResultId));
        var archivalGroupEvent = SeedEvent(storageResultId);
        context.ArchivalGroupEvents.Add(archivalGroupEvent);
        await context.SaveChangesAsync();

        var activity = await FindActivity(context, archivalGroupEvent.ArchivalGroup);

        activity.Object.SeeAlso.Should().NotBeNull().And.HaveCount(1);
        activity.Object.SeeAlso![0].Id.Should().Be(
            new Uri($"https://preservation.test/deposits/{depositId}/importjobs/results/{importJobId}"),
            "a reader following seeAlso must land on Preservation API's own result route, not on Storage API");
    }

    [Fact]
    public async Task An_Event_Whose_Import_Job_Row_Is_Gone_Omits_SeeAlso()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var storageResultId = new Uri($"https://storage.test/importjobs/{Guid.NewGuid():N}");
        // Deliberately no matching row in ImportJobs.
        var archivalGroupEvent = SeedEvent(storageResultId);
        context.ArchivalGroupEvents.Add(archivalGroupEvent);
        await context.SaveChangesAsync();

        var activity = await FindActivity(context, archivalGroupEvent.ArchivalGroup);

        activity.Object.SeeAlso.Should().BeNull(
            "there is nothing on Preservation API for seeAlso to point at, and the raw Storage API " +
            "URI is not something any Preservation API caller can resolve");
    }

    private static ArchivalGroupEvent SeedEvent(Uri storageResultId) => new()
    {
        EventDate = DateTime.UtcNow.AddYears(1),
        ArchivalGroup = new Uri($"https://preservation.test/repository/cc/{Guid.NewGuid():N}"),
        ImportJobResult = storageResultId,
        ToVersion = "v1"
    };

    private static ImportJob SeedImportJob(string depositId, string importJobId, Uri storageResultId) => new()
    {
        Id = importJobId,
        Deposit = depositId,
        StorageImportJobResultId = storageResultId,
        ArchivalGroup = new Uri("https://storage.test/repository/cc/thing"),
        LastUpdated = DateTime.UtcNow,
        ImportJobJson = "{}",
        LatestStorageApiResultJson = "{}",
        LatestPreservationApiResultJson = "{}"
    };

    private static async Task<DigitalPreservation.Common.Model.ChangeDiscovery.Activity> FindActivity(
        PreservationContext context, Uri archivalGroup)
    {
        var handler = new GetArchivalGroupsOrderedCollectionPageHandler(
            NullLogger<GetArchivalGroupsOrderedCollectionPageHandler>.Instance, Mutator(), context);

        var pageNumber = 1;
        while (true)
        {
            var result = await handler.Handle(
                new GetArchivalGroupsOrderedCollectionPage(pageNumber), default);
            result.Success.Should().BeTrue();
            var match = result.Value!.OrderedItems?.SingleOrDefault(a => a.Object.Id == archivalGroup);
            if (match is not null)
            {
                return match;
            }
            if (result.Value.Next is null)
            {
                throw new InvalidOperationException($"Seeded event for {archivalGroup} not found in any page");
            }
            pageNumber++;
        }
    }

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));
}
