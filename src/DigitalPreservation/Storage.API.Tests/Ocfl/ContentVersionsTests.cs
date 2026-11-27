using DigitalPreservation.Common.Model.Storage.Ocfl;
using Storage.API.Ocfl;
using OcflVersion = DigitalPreservation.Common.Model.Storage.Ocfl.Version;

namespace Storage.API.Tests.Ocfl;

/// <summary>
/// Issue #261: the version each file last changed in, from the inventory. Not the version in its
/// content path, which OCFL's deduplication can make older than the change.
/// </summary>
public class ContentVersionsTests
{
    private static OcflVersion V(int day, params (string Digest, string Path)[] files) => new()
    {
        Created = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
        User = new User { Name = "test" },
        State = files.GroupBy(f => f.Digest).ToDictionary(g => g.Key, g => g.Select(f => f.Path).ToArray())
    };

    private static Inventory Inventory(params (string Name, OcflVersion Version)[] versions) => new()
    {
        Id = "ag",
        DigestAlgorithm = "sha512",
        Head = versions[^1].Name,
        ContentDirectory = "content",
        Manifest = new Dictionary<string, string[]>
        {
            ["X"] = ["v1/content/objects/a.tif"],
            ["Y"] = ["v2/content/objects/a.tif"],
            ["B"] = ["v1/content/objects/b.tif"],
            ["C"] = ["v3/content/objects/c.tif"]
        },
        Versions = versions.ToDictionary(v => v.Name, v => v.Version)
    };

    [Fact]
    public void Content_Reverted_To_Earlier_Bytes_Changed_In_The_Version_It_Was_Reverted_In()
    {
        // a.tif is X in v1, Y in v2 and X again in v3. OCFL stores X once, so a.tif's content path in
        // v3 is v1/content/...; but its content changed in v3, and a builder must ingest it again.
        var inventory = Inventory(
            ("v1", V(1, ("X", "objects/a.tif"), ("B", "objects/b.tif"))),
            ("v2", V(2, ("Y", "objects/a.tif"), ("B", "objects/b.tif"))),
            ("v3", V(3, ("X", "objects/a.tif"), ("B", "objects/b.tif"), ("C", "objects/c.tif"))));

        var versions = ContentVersions.Of(inventory, "v3");

        versions["objects/a.tif"].Should().Be("v3", "its content changed back to X in v3");
        versions["objects/b.tif"].Should().Be("v1", "unchanged since v1");
        versions["objects/c.tif"].Should().Be("v3", "new in v3");
    }

    [Fact]
    public void A_Path_Missing_From_A_Version_Starts_Again_When_It_Returns()
    {
        // b.tif is removed in v2 and comes back with the same bytes in v3: new again in v3.
        var inventory = Inventory(
            ("v1", V(1, ("B", "objects/b.tif"))),
            ("v2", V(2, ("X", "objects/a.tif"))),
            ("v3", V(3, ("B", "objects/b.tif"))));

        ContentVersions.Of(inventory, "v3")["objects/b.tif"].Should().Be("v3");
    }

    [Fact]
    public void An_Earlier_Version_Is_Worked_Out_As_At_That_Version()
    {
        var inventory = Inventory(
            ("v1", V(1, ("X", "objects/a.tif"))),
            ("v2", V(2, ("Y", "objects/a.tif"))),
            ("v3", V(3, ("X", "objects/a.tif"))));

        ContentVersions.Of(inventory, "v2")["objects/a.tif"].Should().Be("v2");
        ContentVersions.Of(inventory, "v1")["objects/a.tif"].Should().Be("v1");
    }

    [Fact]
    public void Versions_Are_Ordered_By_Number_Not_By_Name()
    {
        var versions = Enumerable.Range(1, 10)
            .Select(n => ($"v{n}", V(n, (n < 10 ? "X" : "Y", "objects/a.tif"))))
            .ToArray();

        ContentVersions.Of(Inventory(versions), "v10")["objects/a.tif"].Should().Be("v10");
        ContentVersions.Of(Inventory(versions), "v9")["objects/a.tif"].Should().Be("v1");
    }
}
