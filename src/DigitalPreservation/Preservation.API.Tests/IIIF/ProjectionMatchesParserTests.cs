using System.Xml.Linq;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using IIIF.Presentation.V3;
using IIIF.Serialisation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DigitalPreservation.Common.Model.AccessConditions;
using Newtonsoft.Json.Linq;
using Preservation.API.IIIF;
using DigitalPreservation.Mets.StorageImpl;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// What a canvas says about access must be exactly what the Preservation UI shows for that file:
/// the parser's effective values, after inheritance (issue #261). This runs a METS with inheritance
/// through the real MetsParser and compares every canvas with its file, so the projection can't
/// drift from what an archivist saw on the deposit.
/// </summary>
public class ProjectionMatchesParserTests
{
    private const string Ns = """xmlns:mets="http://www.loc.gov/METS/" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:premis="http://www.loc.gov/premis/v3" xmlns:mods="http://www.loc.gov/mods/v3" """;

    private static string Mods(string id, string content) => $"""
        <mets:dmdSec ID="{id}"><mets:mdWrap MDTYPE="MODS"><mets:xmlData><mods:mods>{content}</mods:mods></mets:xmlData></mets:mdWrap></mets:dmdSec>
        """;

    private static readonly string Mets = $"""
        <mets:mets {Ns}>
          {Mods("DMD_objects", """
            <mods:accessCondition type="restriction on access">PA1</mods:accessCondition>
            <mods:accessCondition type="use and reproduction">http://rightsstatements.org/vocab/InC/1.0/</mods:accessCondition>
            <mods:accessCondition type="copyright statement">inc-low-risk</mods:accessCondition>
            <mods:accessCondition type="tier">1</mods:accessCondition>
            <mods:recordInfo><mods:recordIdentifier source="EMu">MS 1</mods:recordIdentifier></mods:recordInfo>
            """)}
          {Mods("DMD_b", """
            <mods:accessCondition type="restriction on access">PA3</mods:accessCondition>
            <mods:accessCondition type="copyright statement">inc-low-risk-orphan</mods:accessCondition>
            <mods:accessCondition type="tier">3</mods:accessCondition>
            """)}
          {Mods("DMD_LOG_1", """
            <mods:recordInfo><mods:recordIdentifier source="EMu">MS 1/1</mods:recordIdentifier></mods:recordInfo>
            """)}
          <mets:fileSec>
            <mets:fileGrp>
              <mets:file ID="FILE_a" MIMETYPE="image/tiff"><mets:FLocat xlink:href="objects/a.tif"/></mets:file>
              <mets:file ID="FILE_b" MIMETYPE="image/tiff"><mets:FLocat xlink:href="objects/b.tif"/></mets:file>
              <mets:file ID="FILE_c" MIMETYPE="image/tiff"><mets:FLocat xlink:href="objects/sub/c.tif"/></mets:file>
            </mets:fileGrp>
          </mets:fileSec>
          <mets:structMap TYPE="PHYSICAL">
            <mets:div ID="PHYS_ROOT" TYPE="Directory" LABEL="__ROOT">
              <mets:div ID="PHYS_objects" TYPE="Directory" LABEL="objects" DMDID="DMD_objects">
                <mets:div ID="PHYS_a" TYPE="Item" LABEL="a.tif"><mets:fptr FILEID="FILE_a"/></mets:div>
                <mets:div ID="PHYS_b" TYPE="Item" LABEL="b.tif" DMDID="DMD_b"><mets:fptr FILEID="FILE_b"/></mets:div>
                <mets:div ID="PHYS_sub" TYPE="Directory" LABEL="sub">
                  <mets:div ID="PHYS_c" TYPE="Item" LABEL="c.tif"><mets:fptr FILEID="FILE_c"/></mets:div>
                </mets:div>
              </mets:div>
            </mets:div>
          </mets:structMap>
          <mets:structMap TYPE="LOGICAL">
            <mets:div ID="LOG_1" TYPE="Item" LABEL="First" DMDID="DMD_LOG_1">
              <mets:fptr FILEID="FILE_a"/>
            </mets:div>
          </mets:structMap>
        </mets:mets>
        """;

    private readonly MetsFileWrapper wrapper;
    private readonly JObject manifest;

    // The Leeds profile as deployed, for what each statement publishes in IIIF.
    private static readonly StatementProfile Statements = AccessConditionProfile.FromJson(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AccessConditionProfiles", "leeds.json"))).Statements!;

    public ProjectionMatchesParserTests()
    {
        var parser = new MetsParser(new FileSystemMetsLoader(), NullLogger<MetsParser>.Instance);
        var result = parser.GetMetsFileWrapperFromXDocument(new Uri("file:///test/mets.xml"), XDocument.Parse(Mets));
        result.Success.Should().BeTrue(result.CodeAndMessage());
        wrapper = result.Value!;

        var projected = new Manifest { Id = "https://pres.example/repository/ag?view=iiif" };
        new ManifestProjection(Options.Create(new IiifOptions())).Project(projected, wrapper, new ProjectionContext
        {
            Kind = ManifestKind.Vanilla,
            IdBase = "https://pres.example/iiif/ag/",
            PlaceholderBase = "https://pres.example/placeholders/",
            PathPrefix = "ag",
            DerivedTypes = ["tier"],
            Statements = Statements
        });
        manifest = JObject.Parse(projected.AsJson());
    }

    private JToken CanvasFor(string localPath) =>
        manifest["items"]!.Single(c => c["id"]!.ToString().EndsWith(IiifIds.ForPath($"ag/{localPath}")));

    private static List<(string Type, string Value, bool Derived)> FromParser(WorkingFile file)
    {
        var expected = file.EffectiveAccessRestrictions.Select(r => ("restriction on access", r, false)).ToList();
        if (file.EffectiveRightsStatement != null)
        {
            expected.Add(("use and reproduction", file.EffectiveRightsStatement.ToString(), false));
        }
        expected.AddRange(file.EffectiveOtherAccessConditions.Select(kv => (kv.Key, kv.Value, kv.Key == "tier")));
        return expected;
    }

    private static List<(string Type, string Value, bool Derived)> FromCanvas(JToken canvas) =>
        (canvas["accessConditions"] as JArray ?? [])
        .Select(c => (c["type"]!.ToString(), c["value"]!.ToString(), c["derived"]?.Value<bool>() ?? false))
        .ToList();

    [Theory]
    [InlineData("objects/a.tif")]
    [InlineData("objects/b.tif")]
    [InlineData("objects/sub/c.tif")]
    public void Every_Canvas_Says_Exactly_What_The_Parser_Says_For_Its_File(string localPath)
    {
        var file = wrapper.Files.Single(f => f.LocalPath == localPath);
        var canvas = CanvasFor(localPath);

        FromCanvas(canvas).Should().Equal(FromParser(file));
        canvas["rights"]?.ToString().Should().Be(file.EffectiveRightsStatement?.ToString());

        var recordIds = canvas["metadata"]!
            .Where(p => p["label"]!["en"]![0]!.ToString().StartsWith(ManifestProjection.LabelPrefix + "Record identifier"))
            .Select(p => p["value"]!["none"]![0]!.ToString());
        recordIds.Should().Equal(file.EffectiveRecordInfo?.RecordIdentifiers.Select(r => r.Value) ?? []);

        // The wording: the manifest's applies to the canvas, so a canvas has its own only where it differs.
        var own = Statements.IiifRightsFor(file.EffectiveOtherAccessConditions.GetValueOrDefault(Statements.Type))?.RequiredStatement;
        var objects = Statements.IiifRightsFor("inc-low-risk")!.RequiredStatement;
        canvas["requiredStatement"]?["value"]?["en"]?[0]?.ToString().Should().Be(own == objects ? null : own);
    }

    [Fact]
    public void Only_The_Canvas_Whose_Statement_Differs_Has_Its_Own_Wording()
    {
        CanvasFor("objects/a.tif")["requiredStatement"].Should().BeNull();
        CanvasFor("objects/sub/c.tif")["requiredStatement"].Should().BeNull();
        CanvasFor("objects/b.tif")["requiredStatement"]!["value"]!["en"]![0]!.ToString()
            .Should().Be(Statements.IiifRightsFor("inc-low-risk-orphan")!.RequiredStatement);
    }

    [Fact]
    public void The_Manifest_Has_The_Objects_Statement_Wording_Under_Its_Label()
    {
        // objects/ has inc-low-risk: what the access dialogue's "Preview IIIF rights" shows for it.
        var entry = Statements.IiifRightsFor("inc-low-risk")!;
        var required = manifest["requiredStatement"]!;

        required["label"]!["en"]![0]!.ToString().Should().Be(entry.RequiredStatementLabel ?? Statements.Iiif!.RequiredStatementLabel);
        required["value"]!["en"]![0]!.ToString().Should().Be(entry.RequiredStatement).And.StartWith("This material is in copyright");
    }

    [Fact]
    public void The_Inheritance_Really_Happened()
    {
        // Pinned by value, so the comparison above can't pass by both sides being empty.
        FromCanvas(CanvasFor("objects/sub/c.tif")).Should().Equal(
            ("restriction on access", "PA1", false),
            ("use and reproduction", "http://rightsstatements.org/vocab/InC/1.0/", false),
            ("copyright statement", "inc-low-risk", false),
            ("tier", "1", true));
        FromCanvas(CanvasFor("objects/b.tif")).Should().Contain(("restriction on access", "PA3", false))
            .And.Contain(("tier", "3", true))
            .And.Contain(("copyright statement", "inc-low-risk-orphan", false));
    }
}
