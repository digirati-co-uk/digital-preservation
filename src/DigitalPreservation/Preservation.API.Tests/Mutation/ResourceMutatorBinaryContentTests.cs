using DigitalPreservation.Common.Model;
using Microsoft.Extensions.Options;
using Preservation.API.Mutation;

namespace Preservation.API.Tests.Mutation;

/// <summary>
/// The Storage API gives every Binary a content URI on its own host, which callers of the
/// Preservation API can't reach, and the Preservation API has no content route (issue #300).
/// So the Preservation API doesn't pass it on: a file is fetched by requesting a download.
/// </summary>
public class ResourceMutatorBinaryContentTests
{
    private static readonly Uri StorageContent = new("https://storage.test/content/cc/thing/objects/a.tif");

    [Fact]
    public void A_Binary_Loses_Its_Storage_Content_Uri()
    {
        var binary = MakeBinary("https://storage.test/repository/cc/thing/objects/a.tif");

        Mutator().MutateStorageResource(binary);

        binary.Content.Should().BeNull();
        binary.Id.Should().Be(new Uri("https://preservation.test/repository/cc/thing/objects/a.tif"),
            "the rest of the resource is rewritten as before");
    }

    [Fact]
    public void Binaries_Inside_Containers_Lose_It_Too()
    {
        var nested = MakeBinary("https://storage.test/repository/cc/thing/objects/sub/b.tif");
        var direct = MakeBinary("https://storage.test/repository/cc/thing/objects/a.tif");
        var container = new Container
        {
            Id = new Uri("https://storage.test/repository/cc/thing/objects"),
            Binaries = [direct],
            Containers =
            [
                new Container
                {
                    Id = new Uri("https://storage.test/repository/cc/thing/objects/sub"),
                    Binaries = [nested]
                }
            ]
        };

        Mutator().MutateStorageResource(container);

        direct.Content.Should().BeNull();
        nested.Content.Should().BeNull();
    }

    [Fact]
    public void The_Property_Is_Left_Out_Of_The_Json_Not_Written_As_Null()
    {
        var binary = MakeBinary("https://storage.test/repository/cc/thing/objects/a.tif");
        System.Text.Json.JsonSerializer.Serialize(binary).Should().Contain("\"content\"",
            "the Storage API still says where it serves the file");

        Mutator().MutateStorageResource(binary);

        System.Text.Json.JsonSerializer.Serialize(binary).Should().NotContain("\"content\"");
    }

    private static Binary MakeBinary(string id) => new()
    {
        Id = new Uri(id),
        Content = StorageContent
    };

    private static ResourceMutator Mutator() => new(Options.Create(new MutatorOptions
    {
        Storage = "https://storage.test",
        Preservation = "https://preservation.test"
    }));
}
