using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using FakeItEasy;
using Preservation.API.Features.Repository.Requests;
using Preservation.API.Tests.TestingInfrastructure;
using Storage.Client;

namespace Preservation.API.Tests.Mutation;

/// <summary>
/// ?view=lightweight passed the Storage API's resource through untouched: its URIs were on the
/// Storage API's host, which callers of this API can't reach, and it had no IIIF links (#261).
/// </summary>
public class LightweightResourceMutationTests
{
    private readonly IStorageApiClient storage = A.Fake<IStorageApiClient>();

    private GetLightweightResourceHandler Handler() => new(storage, TestMutator.Make());

    private void StorageReturns(PreservedResource resource) =>
        A.CallTo(() => storage.GetLightweightResource(A<string>._, A<string?>._))
            .Returns(Result.Ok<PreservedResource?>(resource));

    [Fact]
    public async Task The_Lightweight_View_Has_Preservation_Api_Uris_And_Iiif_Links()
    {
        StorageReturns(new ArchivalGroup { Id = new Uri("https://storage.test/repository/cc/thing") });

        var result = await Handler().Handle(new GetLightweightResource("cc/thing", null), CancellationToken.None);

        var group = result.Value.Should().BeOfType<ArchivalGroup>().Subject;
        group.Id.Should().Be(new Uri("https://preservation.test/repository/cc/thing"));
        group.IiifCs.Should().Be(new Uri("https://preservation.test/repository/cc/thing?view=iiif-cs"));
    }

    [Fact]
    public async Task An_Earlier_Version_Has_No_Iiif_Links_Because_The_Views_Show_The_Current_One()
    {
        StorageReturns(new ArchivalGroup { Id = new Uri("https://storage.test/repository/cc/thing") });

        var result = await Handler().Handle(new GetLightweightResource("cc/thing", "v1"), CancellationToken.None);

        var group = (ArchivalGroup)result.Value!;
        group.Id.Should().Be(new Uri("https://preservation.test/repository/cc/thing"));
        group.Iiif.Should().BeNull();
        group.IiifCs.Should().BeNull();
    }
}
