using DigitalPreservation.Common.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Activity.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;

namespace Preservation.API.Tests.Features.Activity;

/// <summary>
/// Neither API's page handler used to reject a page number below 1 (issue #388 item 2.8's second
/// Diverged finding). This pins the fix for Preservation API's own copy - Storage API's is pinned
/// alongside its other Activity Stream fixes in GetImportJobsOrderedCollectionPageHandlerTests.
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class GetArchivalGroupsOrderedCollectionPageHandlerTests(DatabaseFixture fixture)
{
    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_Page_Below_1_Is_Refused_As_A_Bad_Request(int page)
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var handler = new GetArchivalGroupsOrderedCollectionPageHandler(
            NullLogger<GetArchivalGroupsOrderedCollectionPageHandler>.Instance, Mutator(), context);

        var result = await handler.Handle(new GetArchivalGroupsOrderedCollectionPage(page), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
    }
}
