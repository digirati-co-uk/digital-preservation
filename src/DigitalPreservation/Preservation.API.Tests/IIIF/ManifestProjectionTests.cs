using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using IIIF.Presentation.V3;
using IIIF.Serialisation;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

/// <summary>The IIIF projection of a METS file (issue #261), asserted on the JSON a builder receives.</summary>
public class ManifestProjectionTests
{
    private const string Root = "https://pres.example";
    private const string IdBase = Root + "/iiif/library/ms-1/";
    private const string Placeholders = Root + "/placeholders/";
    private const string AgPath = "library/ms-1";

    private readonly ManifestProjection projection = new(Options.Create(new IiifOptions()));

    // ─── fixtures ───────────────────────────────────────────────────────────────

    private static WorkingFile File(string localPath, string contentType, int? width = null, int? height = null,
        double? duration = null, string? pronom = "fmt/353")
    {
        var file = new WorkingFile
        {
            LocalPath = localPath,
            Name = localPath.Split('/')[^1],
            ContentType = contentType,
            Digest = "abc123",
            Size = 1234567
        };
        file.Metadata.Add(new FileFormatMetadata
        {
            Source = "METS", PronomKey = pronom, FormatName = "Tagged Image File Format", ContentType = contentType
        });
        file.Metadata.Add(new VirusScanMetadata
        {
            Source = "ClamAV", HasVirus = false, Timestamp = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc)
        });
        if (width.HasValue || duration.HasValue)
        {
            file.Metadata.Add(new ExtentMetadata { Source = "METS", PixelWidth = width, PixelHeight = height, Duration = duration });
        }
        return file;
    }

    private static MetsFileWrapper Mets(IEnumerable<WorkingFile> files, IEnumerable<LogicalRange>? ranges = null)
    {
        var fileList = files.ToList();
        var root = new WorkingDirectory { LocalPath = "", Name = "root" };
        var objects = new WorkingDirectory { LocalPath = "objects", Name = "objects" };
        root.Directories.Add(objects);
        foreach (var file in fileList)
        {
            var folder = objects;
            var segments = file.LocalPath.Split('/');
            for (var i = 1; i < segments.Length - 1; i++)
            {
                var path = string.Join('/', segments.Take(i + 1));
                var next = folder.Directories.FirstOrDefault(d => d.LocalPath == path);
                if (next == null)
                {
                    next = new WorkingDirectory { LocalPath = path, Name = segments[i] };
                    folder.Directories.Add(next);
                }
                folder = next;
            }
            folder.Files.Add(file);
        }
        return new MetsFileWrapper { Files = fileList, PhysicalStructure = root, LogicalStructures = ranges?.ToList() ?? [] };
    }

    private static ProjectionContext Vanilla(Func<WorkingFile, string?>? rendering = null, string[]? derived = null) => new()
    {
        Kind = ManifestKind.Vanilla,
        IdBase = IdBase,
        PlaceholderBase = Placeholders,
        PathPrefix = AgPath,
        Rendering = rendering,
        DerivedTypes = derived ?? []
    };

    private static ProjectionContext IiifCs() => new()
    {
        Kind = ManifestKind.IiifCs,
        IdBase = IdBase,
        PlaceholderBase = Placeholders,
        PathPrefix = AgPath,
        Origin = f => $"s3://ocfl/root/v1/content/{f.LocalPath}"
    };

    private JObject Project(MetsFileWrapper mets, ProjectionContext context)
    {
        var manifest = new Manifest { Id = Root + "/repository/library/ms-1?view=iiif" };
        projection.Project(manifest, mets, context);
        return JObject.Parse(manifest.AsJson());
    }

    private static string IdOf(string localPath) => IiifIds.ForPath($"{AgPath}/{localPath}");

    private static string[] Labels(JToken resource) =>
        resource["metadata"]!.Select(p => (string)p["label"]!["en"]![0]!).ToArray();

    private static string? Value(JToken resource, string label) =>
        (string?)resource["metadata"]!.FirstOrDefault(p => (string)p["label"]!["en"]![0]! == label)?["value"]?["none"]?[0];

    // ─── vanilla canvases ───────────────────────────────────────────────────────

    [Fact]
    public void A_Canvas_Is_A_Placeholder_Keyed_By_Format_With_The_Canvas_Ratio()
    {
        var json = Project(Mets([File("objects/page-1.tif", "image/tiff", 3000, 4000)]), Vanilla());
        var canvas = json["items"]![0]!;

        canvas["id"]!.ToString().Should().Be($"{IdBase}canvases/{IdOf("objects/page-1.tif")}");
        canvas["behavior"]!.Values<string>().Should().Equal("placeholder");
        ((int)canvas["width"]!).Should().Be(3000, "the canvas keeps the file's own extents");
        ((int)canvas["height"]!).Should().Be(4000);

        var body = canvas["items"]![0]!["items"]![0]!["body"]!;
        body["id"]!.ToString().Should().Be($"{Placeholders}canvas/750x1000/fmt/353/image/tiff");
        ((int)body["width"]!).Should().Be(750);
        canvas["thumbnail"]![0]!["id"]!.ToString().Should().Be($"{Placeholders}thumb/image");
    }

    [Fact]
    public void A_Canvas_Without_Extents_Takes_The_Placeholders_Size()
    {
        var json = Project(Mets([File("objects/doc.pdf", "application/pdf", pronom: null)]), Vanilla());
        var canvas = json["items"]![0]!;

        ((int)canvas["width"]!).Should().Be(1000);
        ((int)canvas["height"]!).Should().Be(800);
        canvas["duration"].Should().BeNull();
        canvas["items"]![0]!["items"]![0]!["body"]!["id"]!.ToString()
            .Should().Be($"{Placeholders}canvas/1000x800/fmt/0/application/pdf");
    }

    [Fact]
    public void Time_Based_Media_Keeps_Its_Duration()
    {
        var json = Project(Mets([File("objects/clip.mp4", "video/mp4", 1920, 1080, 120.5)]), Vanilla());

        ((double)json["items"]![0]!["duration"]!).Should().Be(120.5);
    }

    [Fact]
    public void A_Deposit_Canvas_Has_No_Rendering_And_An_Archival_Group_Canvas_Links_The_Binary()
    {
        var mets = Mets([File("objects/page-1.tif", "image/tiff", 100, 100)]);

        Project(mets, Vanilla())["items"]![0]!["rendering"].Should().BeNull();

        var rendering = Project(mets, Vanilla(f => $"{Root}/repository/{AgPath}/{f.LocalPath}"))["items"]![0]!["rendering"]![0]!;
        rendering["id"]!.ToString().Should().Be($"{Root}/repository/{AgPath}/objects/page-1.tif");
        rendering["type"]!.ToString().Should().Be("Image");
        rendering["behavior"]!.Values<string>().Should().Equal("original");
    }

    [Fact]
    public void Every_Metadata_Label_Starts_With_The_Preservation_Prefix()
    {
        var json = Project(Mets([File("objects/page-1.tif", "image/tiff", 100, 200)]), Vanilla());
        var canvas = json["items"]![0]!;

        Labels(canvas).Should().OnlyContain(l => l.StartsWith(ManifestProjection.LabelPrefix));
        Value(canvas, "Preservation: PRONOM key").Should().Be("fmt/353");
        Value(canvas, "Preservation: SHA-256").Should().Be("abc123");
        Value(canvas, "Preservation: Path").Should().Be("objects/page-1.tif");
        Value(canvas, "Preservation: Size").Should().StartWith("1,234,567 bytes");
        Value(canvas, "Preservation: Virus check").Should().Be("No virus found, ClamAV, 2026-11-02");
        Value(canvas, "Preservation: Width").Should().Be("100");
    }

    [Fact]
    public void Files_Outside_Objects_Or_Without_A_Media_Type_Have_No_Canvas()
    {
        var noType = File("objects/unknown", "image/tiff");
        noType.ContentType = null;
        var json = Project(Mets([File("metadata/x.xml", "application/xml"), noType, File("objects/a.tif", "image/tiff")]), Vanilla());

        json["items"]!.Should().HaveCount(1);
    }

    [Fact]
    public void The_Original_Name_Is_Shown_Only_When_It_Differs_From_The_Path()
    {
        var same = File("objects/a.tif", "image/tiff");
        same.Metadata.Add(new StorageMetadata { Source = "METS", OriginalName = "objects/a.tif" });
        var different = File("objects/b.tif", "image/tiff");
        different.Metadata.Add(new StorageMetadata { Source = "METS", OriginalName = "Scans/B 001.TIF" });

        var json = Project(Mets([same, different]), Vanilla());

        Value(json["items"]![0]!, "Preservation: Original name").Should().BeNull();
        Value(json["items"]![1]!, "Preservation: Original name").Should().Be("Scans/B 001.TIF");
    }

    [Theory]
    [InlineData("image/tiff", "Image")]
    [InlineData("video/mp4", "Video")]
    [InlineData("audio/mp4", "Sound")]
    [InlineData("text/plain", "Text")]
    [InlineData("application/pdf", "Text")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Text")]
    [InlineData("application/msword", "Text")]
    [InlineData("application/zip", "Dataset")]
    public void The_Rendering_Has_The_Files_Own_Type(string contentType, string expected)
    {
        var json = Project(Mets([File("objects/f", contentType)]), Vanilla(f => $"{Root}/repository/{AgPath}/{f.LocalPath}"));

        json["items"]![0]!["rendering"]![0]!["type"]!.ToString().Should().Be(expected);
    }

    // ─── access conditions ──────────────────────────────────────────────────────

    [Fact]
    public void Effective_Access_Conditions_Are_On_Every_Canvas_With_Derived_Ones_Flagged()
    {
        var file = File("objects/a.tif", "image/tiff");
        file.EffectiveAccessRestrictions = ["Open"];
        file.EffectiveRightsStatement = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        file.EffectiveOtherAccessConditions = new Dictionary<string, string>
        {
            ["copyright statement"] = "In copyright",
            ["Access Tier"] = "2"
        };

        var canvas = Project(Mets([file]), Vanilla(derived: ["Access Tier"]))["items"]![0]!;

        canvas["rights"]!.ToString().Should().Be("http://rightsstatements.org/vocab/InC/1.0/");
        var conditions = canvas[ManifestProjection.AccessConditionsTerm]!;
        conditions.Should().BeEquivalentTo(JArray.Parse("""
            [
              { "type": "restriction on access", "value": "Open" },
              { "type": "use and reproduction", "value": "http://rightsstatements.org/vocab/InC/1.0/" },
              { "type": "copyright statement", "value": "In copyright" },
              { "type": "Access Tier", "value": "2", "derived": true }
            ]
            """));
    }

    [Fact]
    public void Without_A_Statement_There_Is_No_Required_Statement()
    {
        var leeds = DigitalPreservation.Common.Model.AccessConditions.AccessConditionProfile.FromJson(
            System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "AccessConditionProfiles", "leeds.json")));
        var file = File("objects/a.tif", "image/tiff");
        file.EffectiveRightsStatement = new Uri("http://rightsstatements.org/vocab/InC-EDU/1.0/");

        var context = new ProjectionContext
        {
            Kind = ManifestKind.Vanilla, IdBase = IdBase, PlaceholderBase = Placeholders, PathPrefix = AgPath,
            Statements = leeds.Statements
        };
        var mets = Mets([file]);
        mets.PhysicalStructure!.Directories.Single(d => d.LocalPath == "objects").EffectiveRightsStatement =
            new Uri("http://rightsstatements.org/vocab/InC-EDU/1.0/");
        var json = Project(mets, context);

        json["items"]![0]!["rights"]!.ToString().Should().Be("http://rightsstatements.org/vocab/InC-EDU/1.0/",
            "a rights statement from before the profile is still published as rights");
        json["rights"]!.ToString().Should().Be("http://rightsstatements.org/vocab/InC-EDU/1.0/");
        json["requiredStatement"].Should().BeNull();
    }

    [Fact]
    public void The_Manifest_Carries_The_Object_Level_Values_Recorded_On_Objects()
    {
        var mets = Mets([File("objects/a.tif", "image/tiff")]);
        var objects = mets.PhysicalStructure!.Directories.Single(d => d.LocalPath == "objects");
        objects.EffectiveAccessRestrictions = ["Staff only"];
        objects.EffectiveRecordInfo = new RecordInfo { RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = "MS 888" }] };

        var json = Project(mets, Vanilla());

        json[ManifestProjection.AccessConditionsTerm]![0]!["value"]!.ToString().Should().Be("Staff only");
        Value(json, "Preservation: Record identifier (EMu)").Should().Be("MS 888");
    }

    [Fact]
    public void The_Extension_Terms_Are_Scoped_In_The_Head_Context_Before_The_Presentation_Context()
    {
        var vanilla = Project(Mets([File("objects/a.tif", "image/tiff")]), Vanilla());
        var context = (JArray)vanilla["@context"]!;

        context.Should().HaveCount(2);
        context[1]!.ToString().Should().Be("http://iiif.io/api/presentation/3/context.json");
        var terms = (JObject)context[0]!;
        terms[ManifestProjection.AccessConditionsTerm]!["@context"]!.ToString().Should().Be(new IiifOptions().ExtensionContext);
        terms[ManifestProjection.FormerAssetsTerm].Should().BeNull("formerAssets is only in the IIIF-CS manifest");

        var iiifCs = Project(Mets([File("objects/a.tif", "image/tiff")]), IiifCs());
        ((JObject)iiifCs["@context"]![0]!)[ManifestProjection.FormerAssetsTerm].Should().NotBeNull();
    }

    // ─── links ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Linked_File_Is_A_Supplementing_Annotation_Not_A_Canvas()
    {
        var video = File("objects/clip.mp4", "video/mp4", 640, 480, 60);
        video.Links.Add(new FileLink { To = "objects/clip.vtt", Role = FileLinkRoles.FromIiifProvides("closedCaptions") });
        var captions = File("objects/clip.vtt", "text/vtt");

        var json = Project(Mets([video, captions]), Vanilla());

        json["items"]!.Should().HaveCount(1);
        var annotation = json["items"]![0]!["annotations"]![0]!["items"]![0]!;
        annotation["motivation"]!.ToString().Should().Be("supplementing");
        annotation["provides"]!.Values<string>().Should().Equal("closedCaptions");
        // A single body is written as an object, not an array.
        var body = annotation["body"] is JArray bodies ? bodies[0]! : annotation["body"]!;
        body["id"]!.ToString().Should().Be($"{IdBase}files/{IdOf("objects/clip.vtt")}",
            "a Deposit's files are identified, not linked");
        body["type"]!.ToString().Should().Be("Text");
    }

    // ─── ranges ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Logical_Range_References_Canvases_With_Fragments_And_Carries_Its_Access_Conditions()
    {
        var range = new LogicalRange
        {
            Id = "LOG_1", Type = "Chapter", Name = "One",
            EffectiveAccessRestrictions = ["Open"],
            Files = [new FilePointer { LocalPath = "objects/clip.mp4", BeginTime = 10, EndTime = 20.5 }]
        };

        var json = Project(Mets([File("objects/clip.mp4", "video/mp4", 640, 480, 60)], [range]), Vanilla());
        var iiifRange = json["structures"]![0]!;

        iiifRange["id"]!.ToString().Should().Be($"{IdBase}ranges/LOG_1");
        iiifRange["items"]![0]!["id"]!.ToString().Should().Be($"{IdBase}canvases/{IdOf("objects/clip.mp4")}#t=10,20.5");
        Labels(iiifRange).Should().OnlyContain(l => l.StartsWith(ManifestProjection.LabelPrefix));
        iiifRange[ManifestProjection.AccessConditionsTerm]![0]!["value"]!.ToString().Should().Be("Open");
        json["structures"]!.Should().HaveCount(1, "objects/ is flat, so there is no folder range");
    }

    [Theory]
    [InlineData(0.0, 600.0, "#t=0,600")]      // from the very start: still a part, not the whole file
    [InlineData(null, 600.0, "#t=0,600")]
    [InlineData(30.0, null, "#t=30")]
    [InlineData(0.0, null, "")]               // from 0 with no end: the whole file
    [InlineData(null, null, "")]
    [InlineData(30.0, 20.0, "")]              // an end before the begin: no fragment rather than a wrong one
    [InlineData(30.0, 30.0, "")]
    [InlineData(0.00001, 1.5, "#t=0,1.5")]    // plain decimals, to the millisecond, never "1E-05"
    [InlineData(1250.12345, 1900.0, "#t=1250.123,1900")]
    public void A_Time_Range_Keeps_Its_Fragment_Whatever_It_Starts_At(double? begin, double? end, string fragment)
    {
        var range = new LogicalRange
        {
            Id = "LOG_1", Type = "Item", Name = "Interview",
            Files = [new FilePointer { LocalPath = "objects/clip.mp4", BeginTime = begin, EndTime = end }]
        };

        var json = Project(Mets([File("objects/clip.mp4", "video/mp4", 640, 480, 3600)], [range]), Vanilla());

        json["structures"]![0]!["items"]![0]!["id"]!.ToString()
            .Should().Be($"{IdBase}canvases/{IdOf("objects/clip.mp4")}{fragment}");
    }

    [Fact]
    public void Folders_Become_A_Range_When_Content_Sits_In_A_Subfolder_And_It_Is_Kept_Beside_Logical_Ranges()
    {
        var logical = new LogicalRange { Id = "LOG_1", Type = "Book", Files = [new FilePointer { LocalPath = "objects/cover.tif" }] };
        var json = Project(Mets(
        [
            File("objects/cover.tif", "image/tiff"),
            File("objects/letters/1.tif", "image/tiff"),
            File("objects/letters/2.tif", "image/tiff")
        ], [logical]), Vanilla());

        json["structures"]!.Should().HaveCount(2);
        var folders = json["structures"]![1]!;
        folders["label"]!["none"]![0]!.ToString().Should().Be("objects");
        Value(folders, "Preservation: Structure type").Should().Be("Folder");
        folders["items"]![0]!["type"]!.ToString().Should().Be("Canvas");
        var letters = folders["items"]![1]!;
        letters["type"]!.ToString().Should().Be("Range");
        letters["label"]!["none"]![0]!.ToString().Should().Be("letters");
        letters["items"]!.Should().HaveCount(2);
    }

    [Fact]
    public void A_Subfolder_Holding_Only_Linked_Files_Makes_No_Folder_Range()
    {
        var page = File("objects/page-1.tif", "image/tiff");
        page.Links.Add(new FileLink { To = "objects/alto/page-1.xml", Role = FileLinkRoles.FromIiifProvides("supplementing") });

        var json = Project(Mets([page, File("objects/alto/page-1.xml", "application/xml")]), Vanilla());

        json["structures"].Should().BeNull();
    }

    // ─── IIIF-CS ────────────────────────────────────────────────────────────────

    [Fact]
    public void An_IiifCs_Canvas_Has_A_Short_Id_And_No_Painting_Extents_Or_Rendering()
    {
        var json = Project(Mets([File("objects/page-1.tif", "image/tiff", 3000, 4000)]), IiifCs());
        var canvas = json["items"]![0]!;

        canvas["id"]!.ToString().Should().Be(IdOf("objects/page-1.tif"));
        canvas["items"].Should().BeNull("IIIF-CS refuses a matched canvas with painting content");
        canvas["width"].Should().BeNull();
        canvas["rendering"].Should().BeNull();
        canvas["thumbnail"].Should().BeNull();
        canvas["metadata"].Should().NotBeNull();
    }

    [Fact]
    public void Each_Canvas_Has_A_Painted_Resource_With_Its_Asset()
    {
        var json = Project(Mets([File("objects/a.tif", "image/tiff"), File("objects/b.tif", "image/tiff")]), IiifCs());
        var painted = (JArray)json[ManifestProjection.PaintedResourcesProperty]!;

        painted.Should().HaveCount(2);
        var second = painted[1]!;
        second["canvasPainting"]!["canvasId"]!.ToString().Should().Be(json["items"]![1]!["id"]!.ToString());
        ((int)second["canvasPainting"]!["canvasOrder"]!).Should().Be(1, "canvasOrder is the canvas's index in items");
        second["canvasPainting"]!["canvasOriginalId"].Should().BeNull();
        second["asset"]!["id"]!.ToString().Should().Be(IdOf("objects/b.tif"));
        second["asset"]!["mediaType"]!.ToString().Should().Be("image/tiff");
        second["asset"]!["origin"]!.ToString().Should().Be("s3://ocfl/root/v1/content/objects/b.tif");
        second["asset"]!["space"].Should().BeNull("space, delivery channels and roles are the builder's");
    }

    [Fact]
    public void A_Linked_File_Is_An_Adjunct_Of_The_Asset_In_The_IiifCs_Manifest()
    {
        var audio = File("objects/talk.mp3", "audio/mpeg", duration: 300);
        audio.Links.Add(new FileLink { To = "objects/talk.txt", Role = FileLinkRoles.FromIiifProvides("transcript") });

        var json = Project(Mets([audio, File("objects/talk.txt", "text/plain")]), IiifCs());
        var adjunct = json[ManifestProjection.PaintedResourcesProperty]![0]!["asset"]!["adjuncts"]![0]!;

        adjunct["id"]!.ToString().Should().Be(IdOf("objects/talk.txt"));
        adjunct["iiifLink"]!.ToString().Should().Be("inlineAnnotation");
        adjunct["motivation"]!.ToString().Should().Be("supplementing");
        adjunct["provides"]!.ToString().Should().Be("transcript");
        adjunct["origin"]!.ToString().Should().Be("s3://ocfl/root/v1/content/objects/talk.txt");
        json["items"]![0]!["annotations"].Should().BeNull("IIIF-CS makes the annotation from the adjunct");
    }

    [Fact]
    public void Ranges_In_The_IiifCs_Manifest_Reference_The_Short_Canvas_Ids()
    {
        var range = new LogicalRange { Id = "LOG_1", Type = "Chapter", Files = [new FilePointer { LocalPath = "objects/a.tif" }] };

        var json = Project(Mets([File("objects/a.tif", "image/tiff")], [range]), IiifCs());

        json["structures"]![0]!["items"]![0]!["id"]!.ToString().Should().Be(IdOf("objects/a.tif"));
    }

    // --- issue #261 review: ids differing only by case, and empty ranges ---

    [Fact]
    public void Files_Whose_Ids_Differ_Only_By_Case_Get_The_Hash_And_Others_Do_Not()
    {
        // IIIF-CS matches canvas ids ignoring case and refuses a manifest with two that differ only
        // by case. Fedora and S3 are case-sensitive, so A.tif and a.tif can both be preserved.
        var json = Project(Mets([File("objects/A.tif", "image/tiff"), File("objects/a.tif", "image/tiff"), File("objects/b.tif", "image/tiff")]),
            IiifCs());
        var ids = json["items"]!.Select(c => c["id"]!.ToString()).ToList();

        ids.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(3);
        ids[0].Should().MatchRegex("_h[0-9a-f]{12}$");
        ids[1].Should().MatchRegex("_h[0-9a-f]{12}$");
        ids[2].Should().Be(IdOf("objects/b.tif"), "a file with no case twin keeps its plain id");
        json["paintedResources"]!.Select(p => p["asset"]!["id"]!.ToString()).Should().Equal(ids);
    }

    [Fact]
    public void A_Logical_Range_With_Nothing_That_Is_A_Canvas_Is_Left_Out()
    {
        // A Range must have items. The METS range's only file has no media type, so it has no canvas.
        var empty = new LogicalRange
        {
            Id = "LOG_2", Type = "Chapter", Name = "Nothing here",
            Files = [new FilePointer { LocalPath = "objects/untyped.bin" }]
        };
        var parent = new LogicalRange { Id = "LOG_1", Type = "Book", Name = "Book", Ranges = [empty] };
        var full = new LogicalRange
        {
            Id = "LOG_3", Type = "Chapter", Name = "One",
            Files = [new FilePointer { LocalPath = "objects/a.tif" }]
        };
        var untyped = new WorkingFile { LocalPath = "objects/untyped.bin" };

        var json = Project(Mets([File("objects/a.tif", "image/tiff"), untyped], [parent, full]), Vanilla());

        json["structures"]!.Should().HaveCount(1, "LOG_1 contains only the empty LOG_2, so neither is emitted");
        json["structures"]![0]!["id"]!.ToString().Should().EndWith("ranges/LOG_3");
    }

    [Theory]
    [InlineData(10, 20, 110, 220, "#xywh=10,20,100,200")]
    [InlineData(110, 20, 10, 220, "")]          // corners the wrong way round: a negative width
    [InlineData(10, 20, 10, 220, "")]           // no area
    public void A_Region_Fragment_Is_Only_Given_When_It_Makes_Sense(int x1, int y1, int x2, int y2, string fragment)
    {
        var range = new LogicalRange
        {
            Id = "LOG_1", Type = "Figure", Name = "Detail",
            Files = [new FilePointer { LocalPath = "objects/page.tif", Region = new Rectangle { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 } }]
        };

        var json = Project(Mets([File("objects/page.tif", "image/tiff", 1000, 1000)], [range]), Vanilla());

        json["structures"]![0]!["items"]![0]!["id"]!.ToString()
            .Should().Be($"{IdBase}canvases/{IdOf("objects/page.tif")}{fragment}");
    }
}
