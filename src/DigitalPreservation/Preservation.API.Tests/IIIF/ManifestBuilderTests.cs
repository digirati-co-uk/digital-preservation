using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using IIIF.Presentation.V3;
using IIIF.Presentation.V3.Annotation;
using IIIF.Presentation.V3.Content;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

public class ManifestBuilderTests
{
    private const string BaseUrl = "https://example.org/deposits/dep-1/iiif/";
    private const string MediaUrl = "https://example.org/media/tok123/deposit/dep-1/";

    private readonly ManifestBuilder _builder = new();

    // ─── helpers ────────────────────────────────────────────────────────────────

    private static WorkingFile MakeFile(
        string localPath,
        string? contentType,
        int? width = null,
        int? height = null,
        double? duration = null)
    {
        var f = new WorkingFile { LocalPath = localPath, ContentType = contentType };
        if (width.HasValue || height.HasValue || duration.HasValue)
            f.Metadata.Add(new ExtentMetadata { Source = "test", PixelWidth = width, PixelHeight = height, Duration = duration });
        return f;
    }

    private static MetsFileWrapper Wrapper(params WorkingFile[] files) =>
        new() { Files = [.. files] };

    private static MetsFileWrapper WrapperWithRanges(WorkingFile[] files, params LogicalRange[] ranges) =>
        new() { Files = [.. files], LogicalStructures = [.. ranges] };

    private Manifest BuildDeposit(MetsFileWrapper wrapper)
    {
        var manifest = new Manifest { Id = BaseUrl };
        _builder.MakeCanvasesAndRanges(manifest, wrapper, BaseUrl, MediaUrl);
        return manifest;
    }

    private Manifest BuildWithOriginUris(MetsFileWrapper wrapper, Func<WorkingFile, string> resolver)
    {
        var manifest = new Manifest { Id = BaseUrl };
        _builder.MakeCanvasesAndRanges(manifest, wrapper, BaseUrl, null, resolver);
        return manifest;
    }

    private static Canvas SingleCanvas(Manifest manifest) => manifest.Items!.Single();

    private static PaintingAnnotation GetPainting(Canvas canvas) =>
        (PaintingAnnotation)canvas.Items!.Single().Items!.Single();

    private static global::IIIF.Presentation.V3.ResourceBase GetBody(Canvas canvas) =>
        (global::IIIF.Presentation.V3.ResourceBase)GetPainting(canvas).Body!;

    // ─── deposit IIIF: every canvas is a placeholder (issue #261) ───────────────

    public static IEnumerable<object[]> EveryKindOfFile()
    {
        yield return [MakeFile("objects/img.tif", "image/tiff", 2000, 3000)];
        yield return [MakeFile("objects/img-no-extents.tif", "image/tiff")];
        yield return [MakeFile("objects/clip.mp4", "video/mp4", 1920, 1080, 120.0)];
        yield return [MakeFile("objects/track.mp3", "audio/mpeg", duration: 300.0)];
        yield return [MakeFile("objects/doc.pdf", "application/pdf")];
    }

    [Theory]
    [MemberData(nameof(EveryKindOfFile))]
    public void Every_Kind_Of_File_Is_A_Placeholder_With_The_Original_As_Rendering(WorkingFile file)
    {
        var canvas = SingleCanvas(BuildDeposit(Wrapper(file)));

        canvas.Behavior.Should().Equal("placeholder");
        var body = GetBody(canvas).Should().BeOfType<Image>().Subject;
        body.Id.Should().Be($"{MediaUrl}placeholder/canvas.png");
        body.Format.Should().Be("image/png");
        canvas.Thumbnail!.Single().Id.Should().Be($"{MediaUrl}placeholder/thumb.png");

        var rendering = canvas.Rendering!.Single();
        rendering.Id.Should().Be($"{MediaUrl}file/{file.LocalPath}");
        rendering.Format.Should().Be(file.ContentType);
        rendering.Behavior.Should().Equal("original");
    }

    [Fact]
    public void A_Canvas_Keeps_The_Files_Own_Extents()
    {
        // So range fragments stay in the file's coordinates, and the real asset can later be
        // painted on the same canvas.
        var video = SingleCanvas(BuildDeposit(Wrapper(MakeFile("objects/clip.mp4", "video/mp4", 1920, 1080, 120.0))));

        video.Width.Should().Be(1920);
        video.Height.Should().Be(1080);
        video.Duration.Should().Be(120.0);
    }

    [Fact]
    public void A_Canvas_Without_Extents_Takes_The_Placeholders_Size()
    {
        var canvas = SingleCanvas(BuildDeposit(Wrapper(MakeFile("objects/doc.pdf", "application/pdf"))));

        canvas.Width.Should().Be(1000);
        canvas.Height.Should().Be(800);
        canvas.Duration.Should().BeNull();
    }

    [Theory]
    [InlineData("image/tiff", "Image")]
    [InlineData("video/mp4", "Video")]
    [InlineData("audio/mpeg", "Sound")]
    [InlineData("application/pdf", "Text")]
    [InlineData("text/plain", "Text")]
    [InlineData("application/zip", "Dataset")]
    public void The_Rendering_Has_The_Files_Own_Type(string contentType, string expectedType)
    {
        var canvas = SingleCanvas(BuildDeposit(Wrapper(MakeFile("objects/f", contentType))));

        canvas.Rendering!.Single().Type.Should().Be(expectedType);
    }

    // ─── deposit IIIF: filtering ─────────────────────────────────────────────────

    [Fact]
    public void File_OutsideObjectsFolder_IsSkipped()
    {
        var manifest = BuildDeposit(Wrapper(MakeFile("metadata/file.xml", "application/xml")));
        manifest.Items.Should().BeNullOrEmpty();
    }

    [Fact]
    public void File_WithNullContentType_IsSkipped()
    {
        var manifest = BuildDeposit(Wrapper(MakeFile("objects/unknown", null)));
        manifest.Items.Should().BeNullOrEmpty();
    }

    // ─── deposit IIIF: transcript/adjunct ────────────────────────────────────────

    [Fact]
    public void AdjunctFile_IsNotBuiltAsCanvas()
    {
        var main = MakeFile("objects/audio.mp3", "audio/mpeg", duration: 60.0);
        main.Links.Add(new FileLink
        {
            To = "objects/transcript.txt",
            Role = new Uri("http://iiif.io/api/presentation/3#transcript")
        });
        var adjunct = MakeFile("objects/transcript.txt", "text/plain");

        var manifest = BuildDeposit(Wrapper(main, adjunct));

        manifest.Items.Should().HaveCount(1);
        manifest.Items!.Single().Id.Should().Contain("audio.mp3");
    }

    [Fact]
    public void TranscriptLink_CreatesSupplementingAnnotation_WithMediaServerUrl()
    {
        var main = MakeFile("objects/audio.mp3", "audio/mpeg", duration: 60.0);
        main.Links.Add(new FileLink
        {
            To = "objects/t.txt",
            Role = new Uri("http://iiif.io/api/presentation/3#transcript")
        });
        var adjunct = MakeFile("objects/t.txt", "text/plain");

        var manifest = BuildDeposit(Wrapper(main, adjunct));
        var canvas = SingleCanvas(manifest);

        canvas.Annotations.Should().HaveCount(1);
        var anno = (GeneralAnnotation)canvas.Annotations!.Single().Items!.Single();
        anno.Motivation.Should().Be("supplementing");
        anno.Body![0].Id.Should().Be($"{MediaUrl}file/objects/t.txt");
    }

    // ─── deposit IIIF: IDs and encoding ──────────────────────────────────────────

    [Fact]
    public void Canvas_HasExpectedId()
    {
        var manifest = BuildDeposit(Wrapper(MakeFile("objects/img.tif", "image/tiff", 100, 100)));
        SingleCanvas(manifest).Id.Should().Be($"{BaseUrl}canvases/objects/img.tif");
    }

    [Fact]
    public void Canvas_PathElements_AreUrlEncoded()
    {
        var manifest = BuildDeposit(Wrapper(MakeFile("objects/my file (1).tif", "image/tiff", 100, 100)));
        SingleCanvas(manifest).Id.Should().Be($"{BaseUrl}canvases/objects/my%20file%20%281%29.tif");
    }

    [Fact]
    public void Range_HasExpectedId()
    {
        var range = new LogicalRange { Id = "r1", Type = "Chapter", Files = [] };
        var manifest = BuildDeposit(WrapperWithRanges([], range));
        manifest.Structures!.Single().Id.Should().Be($"{BaseUrl}ranges/r1");
    }

    // ─── deposit IIIF: range file pointer fragments ──────────────────────────────

    [Fact]
    public void CanvasRef_WithTemporalFragment_HasHashFragment()
    {
        var file = MakeFile("objects/a.mp3", "audio/mpeg", duration: 60.0);
        var range = new LogicalRange
        {
            Id = "r1", Type = "Chapter",
            Files = [new FilePointer { LocalPath = "objects/a.mp3", BeginTime = 10, EndTime = 30 }]
        };
        var manifest = BuildDeposit(WrapperWithRanges([file], range));
        var canvasRef = (Canvas)manifest.Structures!.Single().Items!.Single();
        canvasRef.Id.Should().EndWith("#t=10,30");
    }

    [Fact]
    public void CanvasRef_WithSpatialFragment_HasXywhFragment()
    {
        var file = MakeFile("objects/img.tif", "image/tiff", 100, 100);
        var range = new LogicalRange
        {
            Id = "r1", Type = "Region",
            Files = [new FilePointer
            {
                LocalPath = "objects/img.tif",
                Region = new Rectangle { X1 = 0, Y1 = 0, X2 = 50, Y2 = 50 }
            }]
        };
        var manifest = BuildDeposit(WrapperWithRanges([file], range));
        var canvasRef = (Canvas)manifest.Structures!.Single().Items!.Single();
        canvasRef.Id.Should().EndWith("#xywh=0,0,50,50");
    }

    [Fact]
    public void CanvasRef_WithNoFragment_HasNoHash()
    {
        var file = MakeFile("objects/img.tif", "image/tiff", 100, 100);
        var range = new LogicalRange
        {
            Id = "r1", Type = "Chapter",
            Files = [new FilePointer { LocalPath = "objects/img.tif" }]
        };
        var manifest = BuildDeposit(WrapperWithRanges([file], range));
        var canvasRef = (Canvas)manifest.Structures!.Single().Items!.Single();
        canvasRef.Id.Should().NotContain("#");
    }

    // ─── AG IIIF: origin URI resolver ────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryKindOfFile))]
    public void OriginUri_Is_The_Rendering_And_The_Canvas_Is_Still_A_Placeholder(WorkingFile file)
    {
        const string origin = "https://host/repository/ag/objects/the-file";

        var canvas = SingleCanvas(BuildWithOriginUris(Wrapper(file), _ => origin));

        canvas.Behavior.Should().Equal("placeholder");
        GetBody(canvas).Id.Should().NotBe(origin, "the original is never painted");
        canvas.Rendering!.Single().Id.Should().Be(origin);
    }

    [Fact]
    public void OriginUri_Transcript_UsesResolvedUriForTranscriptBody()
    {
        var main = MakeFile("objects/audio.mp3", "audio/mpeg", duration: 60.0);
        main.Links.Add(new FileLink
        {
            To = "objects/t.txt",
            Role = new Uri("http://iiif.io/api/presentation/3#transcript")
        });
        var adjunct = MakeFile("objects/t.txt", "text/plain");

        var origins = new Dictionary<string, string>
        {
            ["objects/audio.mp3"] = "https://host/audio.mp3",
            ["objects/t.txt"] = "https://host/t.txt"
        };
        var manifest = BuildWithOriginUris(Wrapper(main, adjunct), f => origins[f.LocalPath]);

        var anno = (GeneralAnnotation)SingleCanvas(manifest).Annotations!.Single().Items!.Single();
        anno.Body![0].Id.Should().Be("https://host/t.txt");
    }
}
