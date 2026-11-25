using DigitalPreservation.Common.Model.Storage.Ocfl;
using Storage.API.Ocfl;
using OcflVersion = DigitalPreservation.Common.Model.Storage.Ocfl.Version;

namespace Storage.API.Tests.Ocfl;

public class FormerFilesTests
{
    private static bool Fedora(string path) => path.StartsWith(".fcrepo/") || path.EndsWith("~fcr-desc.nt");

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
            ["d-a1"] = ["v1/content/objects/a.tif"],
            ["d-a2"] = ["v3/content/objects/a.tif"],
            ["d-b"] = ["v1/content/objects/b.tif"],
            ["d-c"] = ["v2/content/objects/c.tif"],
            ["d-mets"] = ["v1/content/mets.xml"],
            ["d-fc"] = ["v1/content/.fcrepo/fcr-root.json"]
        },
        Versions = versions.ToDictionary(v => v.Name, v => v.Version)
    };

    [Fact]
    public void A_File_In_An_Earlier_Version_And_Not_In_This_One_Is_Former_With_Its_Last_Content()
    {
        var inventory = Inventory(
            ("v1", V(1, ("d-a1", "objects/a.tif"), ("d-b", "objects/b.tif"), ("d-mets", "mets.xml"), ("d-fc", ".fcrepo/fcr-root.json"))),
            ("v2", V(2, ("d-a1", "objects/a.tif"), ("d-c", "objects/c.tif"), ("d-mets", "mets.xml"))),
            ("v3", V(3, ("d-a2", "objects/a.tif"), ("d-mets", "mets.xml"))));

        var former = FormerFiles.Of(inventory, "v3", Fedora);

        former.Keys.Should().BeEquivalentTo("objects/b.tif", "objects/c.tif");
        former["objects/b.tif"].LastVersion.Should().Be("v1");
        former["objects/b.tif"].FullPath.Should().Be("v1/content/objects/b.tif");
        former["objects/c.tif"].LastVersion.Should().Be("v2");
        former["objects/c.tif"].Hash.Should().Be("d-c");
    }

    [Fact]
    public void A_File_Whose_Content_Changed_Is_Not_Former()
    {
        var inventory = Inventory(
            ("v1", V(1, ("d-a1", "objects/a.tif"))),
            ("v2", V(2, ("d-a2", "objects/a.tif"))));

        FormerFiles.Of(inventory, "v2", Fedora).Should().BeEmpty();
    }

    [Fact]
    public void A_File_Removed_Then_Restored_Is_Not_Former()
    {
        var inventory = Inventory(
            ("v1", V(1, ("d-b", "objects/b.tif"))),
            ("v2", V(2, ("d-a1", "objects/a.tif"))),
            ("v3", V(3, ("d-b", "objects/b.tif"), ("d-a1", "objects/a.tif"))));

        FormerFiles.Of(inventory, "v3", Fedora).Should().BeEmpty();
    }

    [Fact]
    public void A_Rename_Shows_The_Old_Path()
    {
        var inventory = Inventory(
            ("v1", V(1, ("d-b", "objects/b.tif"))),
            ("v2", V(2, ("d-b", "objects/renamed.tif"))));

        var former = FormerFiles.Of(inventory, "v2", Fedora);

        former.Keys.Should().Equal("objects/b.tif");
        former["objects/b.tif"].FullPath.Should().Be("v1/content/objects/b.tif");
    }

    [Fact]
    public void Only_Versions_Before_The_Requested_One_Count()
    {
        var inventory = Inventory(
            ("v1", V(1, ("d-b", "objects/b.tif"))),
            ("v2", V(2, ("d-a1", "objects/a.tif"))),
            ("v3", V(3, ("d-c", "objects/c.tif"))));

        var former = FormerFiles.Of(inventory, "v2", Fedora);

        former.Keys.Should().Equal("objects/b.tif"); // c.tif is in a later version, not a former one
    }

    [Fact]
    public void Versions_Are_Ordered_By_Number_Not_Text()
    {
        var versions = Enumerable.Range(1, 10)
            .Select(n => ($"v{n}", n == 9 ? V(n, ("d-b", "objects/b.tif")) : V(n, ("d-a1", "objects/a.tif"))))
            .ToArray();

        var former = FormerFiles.Of(Inventory(versions), "v10", Fedora);

        // As text, "v10" sorts before "v9", which would make v9 look later than v10.
        former["objects/b.tif"].LastVersion.Should().Be("v9");
    }
}
