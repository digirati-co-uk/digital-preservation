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

/// <summary>
/// OCR is published as a seeAlso with the ALTO profile, which text services index, not as a
/// transcript; and the IIIF-CS manifest says which version each file's content was written in, so a
/// builder knows what to ingest again (issue #261).
/// </summary>
public class AltoAndVersionTests
{
    private static JObject Project(MetsFileWrapper mets, ManifestKind kind, Func<WorkingFile, string?>? contentVersion = null,
        string? objectVersion = null)
    {
        var manifest = new Manifest { Id = "https://pres.example/repository/ag?view=iiif" };
        new ManifestProjection(Options.Create(new IiifOptions())).Project(manifest, mets, new ProjectionContext
        {
            Kind = kind,
            IdBase = "https://pres.example/iiif/ag/",
            PlaceholderBase = "https://pres.example/placeholders/",
            PathPrefix = "ag",
            Rendering = kind == ManifestKind.Vanilla ? f => $"https://pres.example/repository/ag/{f.LocalPath}" : null,
            Origin = f => $"s3://ocfl/ag/{contentVersion?.Invoke(f) ?? "v1"}/content/{f.LocalPath}",
            ContentVersion = contentVersion,
            ObjectVersion = objectVersion
        });
        return JObject.Parse(manifest.AsJson());
    }

    private static MetsFileWrapper PageWithOcr(string? profile = FileLinkProfiles.Alto, string? formatName = null)
    {
        var page = new WorkingFile { LocalPath = "objects/0001.tif", Name = "0001.tif", ContentType = "image/tiff" };
        page.Links.Add(new FileLink
        {
            To = "objects/alto/0001.xml",
            Role = FileLinkRoles.FromIiifProvides("transcript"),
            Profile = profile
        });
        var alto = new WorkingFile { LocalPath = "objects/alto/0001.xml", Name = "0001.xml", ContentType = "application/xml" };
        if (formatName != null)
        {
            alto.Metadata.Add(new FileFormatMetadata { Source = "test", FormatName = formatName, ContentType = "application/xml" });
        }
        return new MetsFileWrapper { Files = [page, alto], PhysicalStructure = new WorkingDirectory { LocalPath = "" } };
    }

    [Fact]
    public void Ocr_Is_A_SeeAlso_With_The_Alto_Profile_Not_A_Transcript()
    {
        var canvas = Project(PageWithOcr(), ManifestKind.Vanilla)["items"]![0]!;

        canvas["annotations"].Should().BeNull();
        var seeAlso = canvas["seeAlso"]![0]!;
        seeAlso["id"]!.ToString().Should().Be("https://pres.example/repository/ag/objects/alto/0001.xml");
        seeAlso["profile"]!.ToString().Should().Be(FileLinkProfiles.Alto);
        seeAlso["format"]!.ToString().Should().Be("application/xml");
    }

    [Fact]
    public void An_Ocr_Adjunct_Is_A_SeeAlso_With_The_Alto_Profile()
    {
        var adjunct = Project(PageWithOcr(), ManifestKind.IiifCs)["paintedResources"]![0]!["asset"]!["adjuncts"]![0]!;

        adjunct["iiifLink"]!.ToString().Should().Be("seeAlso");
        adjunct["profile"]!.ToString().Should().Be(FileLinkProfiles.Alto);
        adjunct["motivation"].Should().BeNull();
        adjunct["provides"].Should().BeNull("OCR is not a transcript to show, but text to index");
    }

    [Fact]
    public void Format_Identification_Can_Say_It_Is_Alto_When_The_Mets_Did_Not()
    {
        var canvas = Project(PageWithOcr(profile: null, formatName: "ALTO 3.0"), ManifestKind.Vanilla)["items"]![0]!;

        canvas["seeAlso"]![0]!["profile"]!.ToString().Should().Be(FileLinkProfiles.Alto);
    }

    [Fact]
    public void A_Transcript_That_Is_Not_Alto_Stays_An_Annotation()
    {
        var canvas = Project(PageWithOcr(profile: null), ManifestKind.Vanilla)["items"]![0]!;

        canvas["seeAlso"].Should().BeNull();
        canvas["annotations"]![0]!["items"]![0]!["motivation"]!.ToString().Should().Be("supplementing");
    }

    [Fact]
    public void Every_Asset_And_Adjunct_Says_Which_Version_Its_Content_Was_Written_In()
    {
        var json = Project(PageWithOcr(), ManifestKind.IiifCs,
            contentVersion: f => f.LocalPath.EndsWith(".xml") ? "v2" : "v1",
            objectVersion: "v2");

        json[ManifestProjection.ObjectVersionTerm]!.ToString().Should().Be("v2");
        var asset = json["paintedResources"]![0]!["asset"]!;
        asset[ManifestProjection.ContentVersionProperty]!.ToString().Should().Be("v1", "the image is unchanged since v1");
        asset["adjuncts"]![0]![ManifestProjection.ContentVersionProperty]!.ToString()
            .Should().Be("v2", "the OCR was rewritten in v2, the current version: ingest it again");
        ((JObject)json["@context"]![0]!)[ManifestProjection.ObjectVersionTerm].Should().NotBeNull();
    }

    [Fact]
    public void The_Vanilla_Manifest_Has_No_Versions()
    {
        var json = Project(PageWithOcr(), ManifestKind.Vanilla, contentVersion: _ => "v1", objectVersion: "v1");

        json[ManifestProjection.ObjectVersionTerm].Should().BeNull();
    }
}
