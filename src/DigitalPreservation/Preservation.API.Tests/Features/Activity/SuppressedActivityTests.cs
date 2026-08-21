using DigitalPreservation.Common.Model.ChangeDiscovery;
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
/// An import job can ask for the version it creates to be kept out of the published Activity Stream
/// (issue #188 step 3): a METS ID migration renames identifiers and gives the stream's readers
/// nothing to rebuild, so announcing it as a change would be wasted work and a misleading history.
///
/// Two properties have to hold together, and they pull in opposite directions:
/// the suppressed event must NOT be published, and it must STILL be counted when working out how far
/// we have read Storage API's activities. Get the second wrong and a bulk migration - where every
/// job is suppressed - leaves the watermark behind and the reader re-reads the same window for ever.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class SuppressedActivityTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_Suppressed_Event_Is_Not_Published_But_Still_Moves_The_Watermark()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var published = await SeedEvent(context, suppressed: false);
        var suppressed = await SeedEvent(context, suppressed: true, secondsLater: 1);

        var latest = context.GetLatestArchivalGroupEvent();
        latest!.Id.Should().Be(suppressed.Id,
            "the watermark counts every event, or the Storage API reader never gets past a run of " +
            "suppressed ones");

        var activities = await PublishedActivities(context);
        activities.Should().Contain(a => a.Object.Id == published.ArchivalGroup,
            "an ordinary change is still announced");
        activities.Should().NotContain(a => a.Object.Id == suppressed.ArchivalGroup,
            "a suppressed change is recorded but not announced");
    }

    [Fact]
    public async Task The_Collection_Count_And_The_Pages_Agree_About_Suppressed_Events()
    {
        // The collection's total and the page handler's total between them decide where the pages
        // end. If only one of them filtered, the last page would offer a "next" that is empty - or
        // the collection would claim a last page that does not exist.
        await using var context = fixture.CreateNewAuthServiceContext();
        var before = await TotalItems(context);

        await SeedEvent(context, suppressed: true);
        await SeedEvent(context, suppressed: true, secondsLater: 1);

        (await TotalItems(context)).Should().Be(before,
            "suppressed events do not count towards the published stream");

        var published = await PublishedActivities(context);
        published.Should().HaveCount(before,
            "and the pages contain exactly the events the collection counted");
    }

    // -----------------------------------------------------------------------

    private static async Task<ArchivalGroupEvent> SeedEvent(
        PreservationContext context, bool suppressed, int secondsLater = 0)
    {
        var entity = new ArchivalGroupEvent
        {
            // Ahead of anything already seeded, including the backstop event, so ordering is
            // unambiguous however often this suite has run.
            EventDate = DateTime.UtcNow.AddYears(1).AddSeconds(secondsLater),
            ArchivalGroup = new Uri($"https://preservation.test/repository/cc/{Guid.NewGuid():N}"),
            ImportJobResult = new Uri($"https://storage.test/importjobs/{Guid.NewGuid():N}"),
            FromVersion = "v1",
            ToVersion = "v2",
            Suppressed = suppressed
        };
        context.ArchivalGroupEvents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    private static async Task<int> TotalItems(PreservationContext context)
    {
        var handler = new GetImportJobsOrderedCollectionHandler(Mutator(), context);
        var result = await handler.Handle(new GetArchivalGroupsOrderedCollection(), default);
        result.Success.Should().BeTrue();
        result.Value!.TotalItems.Should().NotBeNull();
        return result.Value.TotalItems!.Value;
    }

    /// <summary>Every activity across every page of the published stream.</summary>
    private static async Task<List<DigitalPreservation.Common.Model.ChangeDiscovery.Activity>>
        PublishedActivities(PreservationContext context)
    {
        var handler = new GetArchivalGroupsOrderedCollectionPageHandler(
            NullLogger<GetArchivalGroupsOrderedCollectionPageHandler>.Instance, Mutator(), context);

        var activities = new List<DigitalPreservation.Common.Model.ChangeDiscovery.Activity>();
        var pageNumber = 1;
        while (true)
        {
            var result = await handler.Handle(
                new GetArchivalGroupsOrderedCollectionPage(pageNumber), default);
            result.Success.Should().BeTrue();
            activities.AddRange(result.Value!.OrderedItems ?? []);
            if (result.Value.Next is null)
            {
                return activities;
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
