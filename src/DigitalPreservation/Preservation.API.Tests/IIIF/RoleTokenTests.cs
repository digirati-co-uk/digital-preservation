using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using IIIF.Presentation.V3;
using IIIF.Serialisation;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// The IIIF-CS manifest gives every asset placeholder roles made from its own access conditions, and
/// linked files (annotation bodies, adjuncts) carry their own conditions (issue #261), so a builder
/// can register assets with roles by substitution, without cross-referencing canvases.
/// </summary>
public class RoleTokenTests
{
    private static WorkingFile File(string localPath, string contentType, string? restriction = null, string? tier = null,
        string? statement = null)
    {
        var file = new WorkingFile { LocalPath = localPath, Name = localPath.Split('/')[^1], ContentType = contentType };
        if (restriction != null) file.EffectiveAccessRestrictions = [restriction];
        file.EffectiveRightsStatement = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        var other = new Dictionary<string, string>();
        if (statement != null) other["copyright statement"] = statement;
        if (tier != null) other["tier"] = tier;
        file.EffectiveOtherAccessConditions = other;
        return file;
    }

    private static MetsFileWrapper Mets(params WorkingFile[] files) => new()
    {
        Files = [.. files],
        PhysicalStructure = new WorkingDirectory { LocalPath = "" }
    };

    private static JObject Project(MetsFileWrapper mets, ManifestKind kind, params string[] roleTypes)
    {
        var projection = new ManifestProjection(Options.Create(new IiifOptions { RoleConditionTypes = [.. roleTypes] }));
        var manifest = new Manifest { Id = "https://pres.example/repository/ag?view=iiif-cs" };
        projection.Project(manifest, mets, new ProjectionContext
        {
            Kind = kind,
            IdBase = "https://pres.example/iiif/ag/",
            PlaceholderBase = "https://pres.example/placeholders/",
            PathPrefix = "ag",
            Rendering = kind == ManifestKind.Vanilla ? f => $"https://pres.example/repository/ag/{f.LocalPath}" : null,
            Origin = f => $"s3://ocfl/ag/v1/content/{f.LocalPath}",
            DerivedTypes = ["tier"]
        });
        return JObject.Parse(manifest.AsJson());
    }

    private static string[] Roles(JToken assetOrAdjunct) => assetOrAdjunct["roles"]!.Values<string>().ToArray()!;

    private static JToken Asset(JObject json, int index = 0) => json["paintedResources"]![index]!["asset"]!;

    [Fact]
    public void By_Default_Every_Condition_But_The_Rights_Statement_Becomes_A_Token()
    {
        var json = Project(Mets(File("objects/a.tif", "image/tiff", restriction: "PA2", tier: "3", statement: "inc-low-risk")),
            ManifestKind.IiifCs);

        Roles(Asset(json)).Should().Equal(
            "urn:x-access-condition:restriction%20on%20access:PA2",
            "urn:x-access-condition:copyright%20statement:inc-low-risk",
            "urn:x-access-condition:tier:3");
    }

    [Fact]
    public void An_Asset_Carries_Its_Conditions_Beside_The_Roles_Made_From_Them()
    {
        var json = Project(Mets(File("objects/a.tif", "image/tiff", restriction: "PA2", tier: "3")), ManifestKind.IiifCs, "tier");

        Asset(json)[ManifestProjection.AccessConditionsTerm].Should().BeEquivalentTo(
            json["items"]![0]![ManifestProjection.AccessConditionsTerm], "the same as its canvas's");
    }

    [Fact]
    public void An_Installation_Can_Name_The_Types_That_Become_Tokens()
    {
        var json = Project(Mets(File("objects/a.tif", "image/tiff", restriction: "PA2", tier: "3", statement: "inc-low-risk")),
            ManifestKind.IiifCs, "tier");

        Roles(Asset(json)).Should().Equal("urn:x-access-condition:tier:3");
    }

    [Fact]
    public void An_Asset_With_None_Of_Those_Conditions_Gets_The_None_Token_Not_No_Roles()
    {
        // Leaving roles empty would publish the file openly if a builder forgot to decide.
        var json = Project(Mets(File("objects/a.tif", "image/tiff", restriction: "PA2")), ManifestKind.IiifCs, "tier");

        Roles(Asset(json)).Should().Equal(ManifestProjection.NoConditionsRoleToken);
    }

    [Fact]
    public void A_Linked_File_Carries_Its_Own_Conditions_In_Both_Manifests()
    {
        var audio = File("objects/talk.mp3", "audio/mpeg", restriction: "PA1", tier: "1");
        audio.Links.Add(new FileLink { To = "objects/talk.txt", Role = FileLinkRoles.FromIiifProvides("transcript") });
        var transcript = File("objects/talk.txt", "text/plain", restriction: "PA4", tier: "4");

        var vanilla = Project(Mets(audio, transcript), ManifestKind.Vanilla, "tier");
        var annotation = vanilla["items"]![0]!["annotations"]![0]!["items"]![0]!;
        var body = annotation["body"] is JArray bodies ? bodies[0]! : annotation["body"]!;
        body[ManifestProjection.AccessConditionsTerm]!.Select(c => c["value"]!.ToString())
            .Should().Contain("PA4").And.Contain("4");

        var iiifCs = Project(Mets(audio, transcript), ManifestKind.IiifCs, "tier");
        var adjunct = Asset(iiifCs)["adjuncts"]![0]!;
        adjunct[ManifestProjection.AccessConditionsTerm]!.Select(c => c["value"]!.ToString()).Should().Contain("PA4");
        Roles(adjunct).Should().Equal("urn:x-access-condition:tier:4");
        ((bool)adjunct[ManifestProjection.AccessDiffersFromAssetProperty]!).Should().BeTrue(
            "IIIF-CS gives an adjunct its asset's access today, so the builder has to know");
    }

    [Fact]
    public void An_Adjunct_With_Its_Assets_Conditions_Is_Not_Flagged()
    {
        var audio = File("objects/talk.mp3", "audio/mpeg", restriction: "PA1", tier: "1");
        audio.Links.Add(new FileLink { To = "objects/talk.txt", Role = FileLinkRoles.FromIiifProvides("transcript") });
        var transcript = File("objects/talk.txt", "text/plain", restriction: "PA1", tier: "1");

        var adjunct = Asset(Project(Mets(audio, transcript), ManifestKind.IiifCs, "tier"))["adjuncts"]![0]!;

        adjunct[ManifestProjection.AccessDiffersFromAssetProperty].Should().BeNull();
        Roles(adjunct).Should().Equal(Roles(Asset(Project(Mets(audio, transcript), ManifestKind.IiifCs, "tier"))));
    }

    [Fact]
    public void The_Same_Conditions_In_A_Different_Order_Are_Not_A_Difference()
    {
        // Inheritance can reach the same set by different routes, in a different order.
        var audio = File("objects/talk.mp3", "audio/mpeg", restriction: "PA1", tier: "1", statement: "inc-low-risk");
        audio.Links.Add(new FileLink { To = "objects/talk.txt", Role = FileLinkRoles.FromIiifProvides("transcript") });
        var transcript = File("objects/talk.txt", "text/plain", restriction: "PA1");
        transcript.EffectiveOtherAccessConditions = new Dictionary<string, string>
        {
            ["tier"] = "1",
            ["copyright statement"] = "inc-low-risk"     // the other way round from the audio's
        };

        var adjunct = Asset(Project(Mets(audio, transcript), ManifestKind.IiifCs, "tier"))["adjuncts"]![0]!;

        adjunct[ManifestProjection.AccessDiffersFromAssetProperty].Should().BeNull();
    }

    [Fact]
    public void A_Value_That_Needs_It_Is_Percent_Encoded()
    {
        var json = Project(Mets(File("objects/a.tif", "image/tiff", restriction: "Staff only: 50% & more")), ManifestKind.IiifCs);

        Roles(Asset(json)).Should().Equal("urn:x-access-condition:restriction%20on%20access:Staff%20only%3A%2050%25%20%26%20more");
    }
}
