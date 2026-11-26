using System.Globalization;
using System.Xml.Linq;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using IIIF.Presentation.V3;
using IIIF.Serialisation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// The Liddle tapes (issue #261): oral history interviews on audio tapes, several to a side, each a
/// logical Item whose mets:area gives its BEGIN and END within a tape. The projection must turn each
/// into a Range over a time fragment of the tape's canvas, and the canvas must have the tape's own
/// duration for that fragment to mean anything. Runs the real METS through the real parser.
/// </summary>
public class LiddleTapesProjectionTests
{
    private static readonly MetsFileWrapper Mets = Parse();

    private static MetsFileWrapper Parse()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "liddle.mets.xml");
        var parser = new MetsParser(new FileSystemMetsLoader(), NullLogger<MetsParser>.Instance);
        var result = parser.GetMetsFileWrapperFromXDocument(new Uri("file:///liddle/mets.xml"), XDocument.Load(path));
        result.Success.Should().BeTrue(result.CodeAndMessage());
        return result.Value!;
    }

    private static JObject Project(ManifestKind kind)
    {
        var manifest = new Manifest { Id = "https://pres.example/repository/liddle/tapes-1-2?view=iiif" };
        new ManifestProjection(Options.Create(new IiifOptions())).Project(manifest, Mets, new ProjectionContext
        {
            Kind = kind,
            IdBase = "https://pres.example/iiif/liddle/tapes-1-2/",
            PlaceholderBase = "https://pres.example/placeholders/",
            PathPrefix = "liddle/tapes-1-2",
            Origin = f => $"s3://ocfl/liddle/v1/content/{f.LocalPath}"
        });
        return JObject.Parse(manifest.AsJson());
    }

    // Every canvas reference in the ranges, however deep: (the canvas id, begin, end).
    private static List<(string CanvasId, double Begin, double End)> TimeFragments(JToken range)
    {
        var found = new List<(string, double, double)>();
        foreach (var item in range["items"] ?? new JArray())
        {
            if (item["type"]!.ToString() == "Range")
            {
                found.AddRange(TimeFragments(item));
                continue;
            }
            var id = item["id"]!.ToString();
            var hash = id.IndexOf("#t=", StringComparison.Ordinal);
            hash.Should().BePositive($"{id} should be a time fragment of a tape");
            var times = id[(hash + 3)..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            found.Add((id[..hash], times[0], times[1]));
        }
        return found;
    }

    private static string IdFor(string localPath) => IiifIds.ForPath($"liddle/tapes-1-2/{localPath}");

    [Fact]
    public void Every_Tape_Is_A_Canvas_With_Its_Own_Duration()
    {
        var canvases = Project(ManifestKind.Vanilla)["items"]!;

        canvases.Should().HaveCount(4);
        canvases.Select(c => (double?)c["duration"]).Should().OnlyContain(d => d > 0,
            "a time fragment means nothing on a canvas without a duration");
    }

    [Fact]
    public void Every_Interview_Is_A_Time_Fragment_Within_Its_Tape()
    {
        var json = Project(ManifestKind.Vanilla);
        var durations = json["items"]!.ToDictionary(c => c["id"]!.ToString(), c => (double)c["duration"]!);
        var fragments = json["structures"]!.SelectMany(TimeFragments).ToList();

        fragments.Should().HaveCount(Mets.LogicalStructures.Sum(CountFilePointers));
        foreach (var (canvasId, begin, end) in fragments)
        {
            durations.Should().ContainKey(canvasId, "every range item points at a canvas in the manifest");
            begin.Should().BeGreaterThanOrEqualTo(0);
            end.Should().BeGreaterThan(begin);
            end.Should().BeLessThanOrEqualTo(durations[canvasId], "an interview can't run past the end of its tape");
        }
    }

    [Fact]
    public void An_Interview_Has_The_Times_Its_Mets_Area_Gives()
    {
        // LOG_0003, ALEXANDER, C: tape 1 side 2, BEGIN 00:20:50, END 00:31:40.
        var json = Project(ManifestKind.Vanilla);
        var alexander = FindRange(json["structures"]!, "ALEXANDER, C")!;

        alexander["items"]!.Single()["id"]!.ToString().Should()
            .Be($"https://pres.example/iiif/liddle/tapes-1-2/canvases/{IdFor("objects/tape1side2.wav")}#t=1250,1900");
    }

    [Fact]
    public void In_The_IiifCs_Manifest_The_Fragments_Are_On_The_Short_Canvas_Ids()
    {
        var json = Project(ManifestKind.IiifCs);
        var canvasIds = json["items"]!.Select(c => c["id"]!.ToString()).ToHashSet();
        var fragments = json["structures"]!.SelectMany(TimeFragments).ToList();

        fragments.Should().NotBeEmpty();
        fragments.Select(f => f.CanvasId).Should().OnlyContain(id => canvasIds.Contains(id),
            "the ranges use the same short ids IIIF-CS rewrites (dlcs/iiif-presentation#726)");
        FindRange(json["structures"]!, "ALEXANDER, C")!["items"]!.Single()["id"]!.ToString()
            .Should().Be($"{IdFor("objects/tape1side2.wav")}#t=1250,1900");
        json["paintedResources"]!.Should().HaveCount(4);
    }

    private static int CountFilePointers(DigitalPreservation.Common.Model.Transit.Extensions.LogicalRange range) =>
        range.Files.Count + range.Ranges.Sum(CountFilePointers);

    private static JToken? FindRange(JToken ranges, string name)
    {
        foreach (var range in ranges.Where(r => r["type"]!.ToString() == "Range"))
        {
            if (range["label"]!["en"]![0]!.ToString().EndsWith(name))
            {
                return range;
            }
            var inner = FindRange(range["items"] ?? new JArray(), name);
            if (inner != null)
            {
                return inner;
            }
        }
        return null;
    }
}
