using DigitalPreservation.Common.Model.Transit.Extensions;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Strings;
using Preservation.API.IIIF;
using Range = IIIF.Presentation.V3.Range;

namespace Preservation.API.Tests.IIIF;

public class ManifestParserTests
{
    private const string BaseUrl = "https://example.org/deposits/dep-1/iiif/";

    // -------------------------------------------------------------------------
    // BuildCanvasIdToLocalPath
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildCanvasIdToLocalPath_MapsCanvasIds_ToLocalPaths()
    {
        var manifest = ManifestWithCanvases(
            $"{BaseUrl}canvases/objects/file.mp4",
            $"{BaseUrl}canvases/objects/sub/image.jpg");

        var result = ManifestParser.BuildCanvasIdToLocalPath(manifest, BaseUrl);

        result.Should().HaveCount(2);
        result[$"{BaseUrl}canvases/objects/file.mp4"].Should().Be("objects/file.mp4");
        result[$"{BaseUrl}canvases/objects/sub/image.jpg"].Should().Be("objects/sub/image.jpg");
    }

    [Fact]
    public void BuildCanvasIdToLocalPath_UnescapesPathElements()
    {
        var escapedId = $"{BaseUrl}canvases/objects/some%20file%28a%29.tif";
        var manifest = ManifestWithCanvases(escapedId);

        var result = ManifestParser.BuildCanvasIdToLocalPath(manifest, BaseUrl);

        result[escapedId].Should().Be("objects/some file(a).tif");
    }

    [Fact]
    public void BuildCanvasIdToLocalPath_IgnoresCanvasesFromOtherDeposits()
    {
        var manifest = ManifestWithCanvases("https://other.org/deposits/dep-99/iiif/canvases/objects/file.mp4");

        var result = ManifestParser.BuildCanvasIdToLocalPath(manifest, BaseUrl);

        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ExtractLogicalRanges — label / id round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractLogicalRanges_UsesMetadata_ToRoundTripTypeNameId()
    {
        var range = MakeRange("ignored-url-id", "Chapter: Opening Credits", []);
        range.Metadata =
        [
            new LabelValuePair("en", "Type", "Chapter"),
            new LabelValuePair("en", "Name", "Opening Credits"),
            new LabelValuePair("en", "id", "existing-mets-id")
        ];
        var manifest = ManifestWithStructures(range);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].Id.Should().Be("existing-mets-id");
        ranges[0].Type.Should().Be("Chapter");
        ranges[0].Name.Should().Be("Opening Credits");
    }

    [Fact]
    public void ExtractLogicalRanges_TreatsEmptyMetaName_AsNull()
    {
        var range = MakeRange("r1", "Chapter: r1", []);
        range.Metadata =
        [
            new LabelValuePair("en", "Type", "Chapter"),
            new LabelValuePair("en", "Name", ""),
            new LabelValuePair("en", "id", "existing-id")
        ];
        var manifest = ManifestWithStructures(range);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].Name.Should().BeNull();
    }

    [Fact]
    public void ExtractLogicalRanges_ExternalRange_UsesLabelAsName_AndDepthAsType()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";
        var childRange = MakeRange("child", "Side B", [new Canvas { Id = canvasId }]);
        var parentRange = MakeRange("parent", "Greatest Hits", [childRange]);
        var manifest = ManifestWithCanvasesAndStructures([canvasId], parentRange);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].Type.Should().Be("Collection");
        ranges[0].Name.Should().Be("Greatest Hits");
        ranges[0].Ranges[0].Type.Should().Be("Item");
        ranges[0].Ranges[0].Name.Should().Be("Side B");
    }

    [Fact]
    public void ExtractLogicalRanges_MintsShortNcNameIds()
    {
        // IDs are always re-minted — they carry no semantic meaning
        var manifest = ManifestWithStructures(
            MakeRange("r1", "Chapter: Title", []));

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].Id.Should().StartWith("r");
        ranges[0].Id.Should().HaveLength(12); // "r" + 11 hex chars
    }

    [Fact]
    public void ExtractLogicalRanges_MintsUniqueIds_ForEachCall()
    {
        var manifest = ManifestWithStructures(MakeRange("r1", "Chapter: Title", []));

        var first  = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());
        var second = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        first[0].Id.Should().NotBe(second[0].Id);
    }

    // -------------------------------------------------------------------------
    // ExtractLogicalRanges — file pointers with hash fragments
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractLogicalRanges_ExtractsCanvasWithTemporalFragment()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";
        var manifest = ManifestWithCanvasesAndStructures(
            [canvasId],
            MakeRange("r1", "Chapter: Act 1",
            [new Canvas { Id = $"{canvasId}#t=10,30" }]));

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].Files.Should().HaveCount(1);
        var fp = ranges[0].Files[0];
        fp.LocalPath.Should().Be("objects/audio.mp3");
        fp.BeginTime.Should().Be(10);
        fp.EndTime.Should().Be(30);
        fp.Region.Should().BeNull();
    }

    [Fact]
    public void ExtractLogicalRanges_ExtractsCanvasWithBeginTimeOnly()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";
        var manifest = ManifestWithCanvasesAndStructures(
            [canvasId],
            MakeRange("r1", "Chapter: Intro", [new Canvas { Id = $"{canvasId}#t=5" }]));

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        var fp = ranges[0].Files[0];
        fp.BeginTime.Should().Be(5);
        fp.EndTime.Should().BeNull();
    }

    [Fact]
    public void ExtractLogicalRanges_ExtractsCanvasWithSpatialFragment()
    {
        var canvasId = $"{BaseUrl}canvases/objects/image.tif";
        var manifest = ManifestWithCanvasesAndStructures(
            [canvasId],
            MakeRange("r1", "Region: Top Left", [new Canvas { Id = $"{canvasId}#xywh=10,20,200,300" }]));

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        var fp = ranges[0].Files[0];
        fp.LocalPath.Should().Be("objects/image.tif");
        fp.Region.Should().NotBeNull();
        var region = fp.Region!;
        region.X1.Should().Be(10);
        region.Y1.Should().Be(20);
        region.X2.Should().Be(210);  // x + w
        region.Y2.Should().Be(320);  // y + h
        fp.BeginTime.Should().BeNull();
    }

    [Fact]
    public void ExtractLogicalRanges_ExtractsCanvasWithCombinedFragment()
    {
        var canvasId = $"{BaseUrl}canvases/objects/video.mp4";
        var manifest = ManifestWithCanvasesAndStructures(
            [canvasId],
            MakeRange("r1", "Scene: One", [new Canvas { Id = $"{canvasId}#t=5,15&xywh=0,0,100,100" }]));

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        var fp = ranges[0].Files[0];
        fp.BeginTime.Should().Be(5);
        fp.EndTime.Should().Be(15);
        var region = fp.Region!;
        region.X1.Should().Be(0);
        region.X2.Should().Be(100);
    }

    [Fact]
    public void ExtractLogicalRanges_ExtractsCanvasWithNoFragment()
    {
        var canvasId = $"{BaseUrl}canvases/objects/image.tif";
        var manifest = ManifestWithCanvasesAndStructures(
            [canvasId],
            MakeRange("r1", "Chapter: All", [new Canvas { Id = canvasId }]));

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        var fp = ranges[0].Files[0];
        fp.LocalPath.Should().Be("objects/image.tif");
        fp.BeginTime.Should().BeNull();
        fp.EndTime.Should().BeNull();
        fp.Region.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ExtractLogicalRanges — SpecificResource with FragmentSelector (raw JSON fallback)
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractLogicalRanges_ExtractsSpecificResource_WithFragmentSelector_TemporalViaRawJson()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";

        // Build a manifest object with a SpecificResource item; the real FragmentSelector
        // data lives only in the raw JSON (iiif-net 0.3.13 doesn't model it).
        var specificResource = new SpecificResource { Source = new Canvas { Id = canvasId } };
        var range = MakeRange("r1", "Chapter: SR", [specificResource]);
        var manifest = ManifestWithCanvasesAndStructures([canvasId], range);

        var rawJson = BuildRawJsonWithFragmentSelector(canvasId, "t=20,40");

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, rawJson);

        var fp = ranges[0].Files[0];
        fp.LocalPath.Should().Be("objects/audio.mp3");
        fp.BeginTime.Should().Be(20);
        fp.EndTime.Should().Be(40);
    }

    [Fact]
    public void ExtractLogicalRanges_ExtractsSpecificResource_WithFragmentSelector_ArraySelector()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";
        var specificResource = new SpecificResource { Source = new Canvas { Id = canvasId } };
        var range = MakeRange("r1", "Chapter: SR", [specificResource]);
        var manifest = ManifestWithCanvasesAndStructures([canvasId], range);

        // Selector as array containing FragmentSelector
        var rawJson = BuildRawJsonWithFragmentSelectorArray(canvasId, "t=0,10");

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, rawJson);

        ranges[0].Files[0].BeginTime.Should().Be(0);
        ranges[0].Files[0].EndTime.Should().Be(10);
    }

    [Fact]
    public void ExtractLogicalRanges_IgnoresSpecificResource_WithoutFragmentSelector()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";
        var specificResource = new SpecificResource { Source = new Canvas { Id = canvasId } };
        var range = MakeRange("r1", "Chapter: SR", [specificResource]);
        var manifest = ManifestWithCanvasesAndStructures([canvasId], range);

        // Raw JSON has a SpecificResource but no selector
        var rawJson = BuildRawJsonWithSpecificResourceNoSelector(canvasId);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, rawJson);

        // Still produces a FilePointer — just without any fragment times
        ranges[0].Files.Should().HaveCount(1);
        ranges[0].Files[0].BeginTime.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ExtractLogicalRanges — access restrictions, rights, record info
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractLogicalRanges_RoundTrips_AccessRestrictions()
    {
        var range = MakeRange("r1", "Section: Restricted", []);
        range.Metadata =
        [
            new LabelValuePair("en", "access restriction", "closed"),
            new LabelValuePair("en", "access restriction", "embargoed")
        ];
        var manifest = ManifestWithStructures(range);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].AccessRestrictions.Should().BeEquivalentTo(["closed", "embargoed"]);
    }

    [Fact]
    public void ExtractLogicalRanges_RoundTrips_RightsStatement()
    {
        var range = MakeRange("r1", "Section: Open", []);
        range.Rights = "https://creativecommons.org/licenses/by/4.0/";
        var manifest = ManifestWithStructures(range);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].RightsStatement.Should().Be(new Uri("https://creativecommons.org/licenses/by/4.0/"));
    }

    [Fact]
    public void ExtractLogicalRanges_RoundTrips_RecordIdentifiers()
    {
        var range = MakeRange("r1", "Section: Identified", []);
        range.Metadata =
        [
            new LabelValuePair("en", "record identifier: voyager", "VYG-001"),
            new LabelValuePair("en", "record identifier: alma", "ALM-999")
        ];
        var manifest = ManifestWithStructures(range);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].RecordInfo.Should().NotBeNull();
        var info = ranges[0].RecordInfo!;
        info.RecordIdentifiers.Should().HaveCount(2);
        info.RecordIdentifiers[0].Source.Should().Be("voyager");
        info.RecordIdentifiers[0].Value.Should().Be("VYG-001");
        info.RecordIdentifiers[1].Source.Should().Be("alma");
    }

    // -------------------------------------------------------------------------
    // ExtractLogicalRanges — nested ranges
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractLogicalRanges_BuildsNestedRangeTree()
    {
        var canvasId = $"{BaseUrl}canvases/objects/audio.mp3";
        var childRange = MakeRange("child", "Track: Side B", [new Canvas { Id = $"{canvasId}#t=60,120" }]);
        var parentRange = MakeRange("parent", "Album: Greatest Hits", [childRange]);
        var manifest = ManifestWithCanvasesAndStructures([canvasId], parentRange);

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges.Should().HaveCount(1);
        ranges[0].Files.Should().BeEmpty();
        ranges[0].Ranges.Should().HaveCount(1);

        var child = ranges[0].Ranges[0];
        child.Files.Should().HaveCount(1);
        child.Files[0].BeginTime.Should().Be(60);
        child.Files[0].EndTime.Should().Be(120);
    }

    [Fact]
    public void ExtractLogicalRanges_SkipsFiles_WhenCanvasNotInManifest()
    {
        // A range references a canvas that is not in manifest.Items
        var unknownCanvas = $"{BaseUrl}canvases/objects/unknown.mp3";
        var range = MakeRange("r1", "Chapter: Ghost", [new Canvas { Id = unknownCanvas }]);
        // Don't add the canvas to manifest.Items
        var manifest = new Manifest { Structures = [range] };

        var ranges = ManifestParser.ExtractLogicalRanges(manifest, BaseUrl, EmptyJson());

        ranges[0].Files.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Manifest ManifestWithCanvases(params string[] canvasIds)
    {
        return new Manifest
        {
            Items = canvasIds.Select(id => new Canvas { Id = id }).ToList<Canvas>()
        };
    }

    private static Manifest ManifestWithStructures(Range range)
    {
        return new Manifest { Structures = [range] };
    }

    private static Manifest ManifestWithCanvasesAndStructures(string[] canvasIds, Range range)
    {
        return new Manifest
        {
            Items = canvasIds.Select(id => new Canvas { Id = id }).ToList<Canvas>(),
            Structures = [range]
        };
    }

    private static Range MakeRange(string id, string label, List<IStructuralLocation> items)
    {
        var fullId = id.StartsWith("http") ? id : $"{BaseUrl}ranges/{id}";
        return new Range
        {
            Id = fullId,
            Label = new LanguageMap("en", label),
            Items = items
        };
    }

    private static string EmptyJson() =>
        """{"@context":"http://iiif.io/api/presentation/3/context.json","type":"Manifest"}""";

    private static string BuildRawJsonWithFragmentSelector(string canvasId, string fragmentValue)
    {
        return $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "type": "Manifest",
          "structures": [
            {
              "type": "Range",
              "id": "{{BaseUrl}}ranges/r1",
              "items": [
                {
                  "type": "SpecificResource",
                  "source": "{{canvasId}}",
                  "selector": {
                    "type": "FragmentSelector",
                    "value": "{{fragmentValue}}"
                  }
                }
              ]
            }
          ]
        }
        """;
    }

    private static string BuildRawJsonWithFragmentSelectorArray(string canvasId, string fragmentValue)
    {
        return $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "type": "Manifest",
          "structures": [
            {
              "type": "Range",
              "id": "{{BaseUrl}}ranges/r1",
              "items": [
                {
                  "type": "SpecificResource",
                  "source": "{{canvasId}}",
                  "selector": [
                    { "type": "PointSelector", "t": 0 },
                    { "type": "FragmentSelector", "value": "{{fragmentValue}}" }
                  ]
                }
              ]
            }
          ]
        }
        """;
    }

    private static string BuildRawJsonWithSpecificResourceNoSelector(string canvasId)
    {
        return $$"""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "type": "Manifest",
          "structures": [
            {
              "type": "Range",
              "id": "{{BaseUrl}}ranges/r1",
              "items": [
                {
                  "type": "SpecificResource",
                  "source": "{{canvasId}}"
                }
              ]
            }
          ]
        }
        """;
    }
}
