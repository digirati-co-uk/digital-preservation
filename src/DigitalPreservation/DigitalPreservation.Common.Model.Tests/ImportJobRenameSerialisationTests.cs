using System.Text.Json;
using DigitalPreservation.Common.Model.Import;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// Every other ImportJob list carries [JsonPropertyName], but ContainersToRename and
/// BinariesToRename had only [JsonPropertyOrder]. With default JsonSerializerOptions (used when
/// serialising the model directly rather than over HTTP, where JsonSerializerDefaults.Web
/// camel-cases everything anyway), the two rename lists came out PascalCase while every other list
/// stayed camelCase (issue #274 item 2).
/// </summary>
public class ImportJobRenameSerialisationTests
{
    private static ImportJob JobWithRenames() => new()
    {
        ContainersToRename = [new Container { Id = new Uri("https://example.com/repo/renamed-folder") }],
        BinariesToRename = [new Binary { Id = new Uri("https://example.com/repo/renamed-file.txt") }]
    };

    [Fact]
    public void Serialises_RenameLists_CamelCase_WithDefaultOptions()
    {
        var json = JsonSerializer.Serialize(JobWithRenames());

        json.Should().Contain("\"containersToRename\"");
        json.Should().Contain("\"binariesToRename\"");
        json.Should().NotContain("\"ContainersToRename\"");
        json.Should().NotContain("\"BinariesToRename\"");
    }

    [Fact]
    public void RoundTrips_RenameLists_WithDefaultOptions()
    {
        var json = JsonSerializer.Serialize(JobWithRenames());

        var deserialised = JsonSerializer.Deserialize<ImportJob>(json);

        deserialised!.ContainersToRename.Should().ContainSingle();
        deserialised.BinariesToRename.Should().ContainSingle();
    }
}
