using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Search;
using FakeItEasy;
using LeedsDlipServices.Identity;
using Microsoft.Extensions.Options;
using Preservation.API.Features.Search.Requests;
using Preservation.API.Mutation;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Client;

namespace Preservation.API.Tests.Features.Search;

/// <summary>
/// SearchCollection declares text and SearchType, echoing the request, but the handler never set
/// either - the UI never noticed because it filled both in itself after the call. A direct API
/// caller got null for both (issue #274 item 3).
/// </summary>
[Collection(DatabaseCollection.CollectionName)]
public class SearchRequestHandlerTests(DatabaseFixture fixture)
{
    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));

    private static IStorageApiClient StorageApiClient()
    {
        var client = A.Fake<IStorageApiClient>();
        A.CallTo(() => client.FedoraSearch(A<string>._, A<int?>._, A<int?>._))
            .Returns(Task.FromResult(Result.Ok<SearchCollectiveFedora>(null)));
        return client;
    }

    private static IIdentityService IdentityService()
    {
        var service = A.Fake<IIdentityService>();
        A.CallTo(() => service.GetIdentityBySchema(A<SchemaAndValue>._, A<CancellationToken>._))
            .Returns(Task.FromResult(Result.FailNotNull<IdentityRecord>(ErrorCodes.NotFound, "not found")));
        A.CallTo(() => service.GetIdentityByCatIrn(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult(Result.FailNotNull<IdentityRecord>(ErrorCodes.NotFound, "not found")));
        return service;
    }

    [Fact]
    public async Task Handle_EchoesTextAndSearchType_FromTheRequest()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var handler = new SearchRequestHandler(StorageApiClient(), context, IdentityService(), Mutator());

        var result = await handler.Handle(
            new SearchRequest("pipeline", type: SearchType.Deposits), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.text.Should().Be("pipeline");
        result.Value.SearchType.Should().Be(SearchType.Deposits);
    }

    [Fact]
    public async Task Handle_SerialisesSearchTypeAsAString()
    {
        await using var context = fixture.CreateNewAuthServiceContext();
        var handler = new SearchRequestHandler(StorageApiClient(), context, IdentityService(), Mutator());

        var result = await handler.Handle(
            new SearchRequest("pipeline", type: SearchType.Deposits), CancellationToken.None);

        // JsonSerializerDefaults.Web (camelCase property names) is what the real API response
        // actually goes through - ASP.NET Core's default JSON options, not this test's plain
        // JsonSerializer.Serialize() default.
        var webOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value, webOptions);
        json.Should().Contain("\"searchType\":\"Deposits\"");
    }
}
