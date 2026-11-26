using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Storage;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.AccessConditions;
using FakeItEasy;
using IIIF.Serialisation;
using MediatR;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Preservation.API.Features.Iiif.Requests;
using Preservation.API.Features.Repository.Requests;
using Preservation.API.IIIF;

namespace Preservation.API.Tests.IIIF;

/// <summary>
/// What the Archival Group handler adds to the projection (issue #261): the Binary URIs as
/// renderings, the OCFL content locations as asset origins, formerAssets and seeAlso.
/// </summary>
public class ArchivalGroupManifestTests
{
    private const string Root = "https://pres.example";
    private const string AgUri = Root + "/repository/library/ms-1";

    private readonly IMediator mediator = A.Fake<IMediator>();
    private readonly IMetsParser metsParser = A.Fake<IMetsParser>();
    private readonly IDerivedAccessConditionResolver resolver = A.Fake<IDerivedAccessConditionResolver>();

    public ArchivalGroupManifestTests()
    {
        A.CallTo(() => mediator.Send(A<GetBinaryStream>._, A<CancellationToken>._))
            .ReturnsLazily(() => Result.OkNotNull<Stream>(new MemoryStream("<mets xmlns=\"http://www.loc.gov/METS/\"/>"u8.ToArray())));
        A.CallTo(() => metsParser.GetMetsFileWrapperFromXDocument(A<Uri>._, A<XDocument>._))
            .Returns(Result.OkNotNull(new MetsFileWrapper
            {
                Files =
                [
                    new WorkingFile { LocalPath = "objects/a.tif", ContentType = "image/tiff" },
                    new WorkingFile { LocalPath = "objects/sub dir/b.tif", ContentType = "image/tiff" }
                ],
                PhysicalStructure = new WorkingDirectory { LocalPath = "" }
            }));
        A.CallTo(() => resolver.GetManagedTypesAsync(A<CancellationToken>._)).Returns(new List<string>());
    }

    private static ArchivalGroup Group(bool withMets = true)
    {
        var subDir = new Container
        {
            Id = new Uri($"{AgUri}/objects/sub%20dir"),
            Binaries = [new Binary { Id = new Uri($"{AgUri}/objects/sub%20dir/b.tif") }]
        };
        var objects = new Container
        {
            Id = new Uri($"{AgUri}/objects"),
            Binaries = [new Binary { Id = new Uri($"{AgUri}/objects/a.tif") }],
            Containers = [subDir]
        };
        var version = new ObjectVersion { OcflVersion = "v3", MementoTimestamp = "20260101000000", MementoDateTime = DateTime.UtcNow };
        return new ArchivalGroup
        {
            Id = new Uri(AgUri),
            Name = "MS 1",
            Containers = [objects],
            Binaries = withMets ? [new Binary { Id = new Uri($"{AgUri}/mets.xml") }] : [],
            StorageMap = new StorageMap
            {
                Version = version,
                HeadVersion = version,
                AllVersions = [version],
                StorageType = "S3",
                Root = "ocfl-bucket",
                ObjectPath = "fedora/ab/cd/library/ms-1",
                ArchivalGroup = new Uri(AgUri),
                Hashes = [],
                Files = new Dictionary<string, OriginFile>
                {
                    ["objects/a.tif"] = new() { Hash = "1", FullPath = "v3/content/objects/a.tif" },
                    ["objects/sub dir/b.tif"] = new() { Hash = "2", FullPath = "v1/content/objects/sub dir/b.tif" },
                    ["mets.xml"] = new() { Hash = "3", FullPath = "v3/content/mets.xml" }
                },
                FormerFiles = new Dictionary<string, FormerOriginFile>
                {
                    ["objects/gone.tif"] = new() { Hash = "4", FullPath = "v1/content/objects/gone.tif", LastVersion = "v2" },
                    ["old-mets.xml"] = new() { Hash = "5", FullPath = "v1/content/old-mets.xml", LastVersion = "v1" }
                }
            }
        };
    }

    private async Task<Result<global::IIIF.Presentation.V3.Manifest>> Handle(ManifestKind kind, ArchivalGroup? group = null)
    {
        var handler = new GetArchivalGroupAsIIIFManifestHandler(mediator, metsParser, resolver,
            A.Fake<IAccessConditionProfileProvider>(),
            new ManifestProjection(Options.Create(new IiifOptions())));
        return await handler.Handle(
            new GetArchivalGroupAsIIIFManifest(group ?? Group(), kind, $"{AgUri}?view=iiif", Root), CancellationToken.None);
    }

    private async Task<JObject> Json(ManifestKind kind)
    {
        var result = await Handle(kind);
        result.Success.Should().BeTrue();
        return JObject.Parse(result.Value!.AsJson());
    }

    [Fact]
    public async Task A_Vanilla_Canvas_Renders_The_Binary_Uri_Not_A_Storage_Location()
    {
        var json = await Json(ManifestKind.Vanilla);

        json["items"]![1]!["rendering"]![0]!["id"]!.ToString().Should().Be($"{AgUri}/objects/sub%20dir/b.tif");
        json.ToString().Should().NotContain("s3://", "the vanilla manifest names no storage location");
        json[ManifestProjection.PaintedResourcesProperty].Should().BeNull();
        json[ManifestProjection.FormerAssetsTerm].Should().BeNull();
    }

    [Fact]
    public async Task The_Ids_Come_From_The_Decoded_Repository_Path()
    {
        var json = await Json(ManifestKind.Vanilla);

        json["items"]![1]!["id"]!.ToString().Should()
            .Be($"{Root}/iiif/library/ms-1/canvases/{IiifIds.ForPath("library/ms-1/objects/sub dir/b.tif")}");
    }

    [Fact]
    public async Task An_Asset_Origin_Is_The_Files_Content_In_Ocfl()
    {
        var json = await Json(ManifestKind.IiifCs);
        var painted = json[ManifestProjection.PaintedResourcesProperty]!;

        painted[0]!["asset"]!["origin"]!.ToString().Should().Be("s3://ocfl-bucket/fedora/ab/cd/library/ms-1/v3/content/objects/a.tif");
        painted[1]!["asset"]!["origin"]!.ToString().Should()
            .Be("s3://ocfl-bucket/fedora/ab/cd/library/ms-1/v1/content/objects/sub dir/b.tif",
                "an unchanged file keeps the content path of the version it was added in");
    }

    [Fact]
    public async Task Former_Assets_Are_The_Former_Files_Below_Objects()
    {
        var json = await Json(ManifestKind.IiifCs);

        json[ManifestProjection.FormerAssetsTerm].Should().BeEquivalentTo(JArray.Parse($$"""
            [
              {
                "id": "{{IiifIds.ForPath("library/ms-1/objects/gone.tif")}}",
                "lastVersion": "v2",
                "origin": "s3://ocfl-bucket/fedora/ab/cd/library/ms-1/v1/content/objects/gone.tif"
              }
            ]
            """));
    }

    [Fact]
    public async Task SeeAlso_Points_At_The_Mets_And_The_Archival_Group()
    {
        var json = await Json(ManifestKind.Vanilla);

        json["seeAlso"]!.Select(s => s["id"]!.ToString()).Should().Equal($"{AgUri}?view=mets", AgUri);
        json["label"]!["none"]![0]!.ToString().Should().Be("MS 1");
    }

    [Fact]
    public async Task An_Archival_Group_Without_Mets_Is_Not_Found()
    {
        var result = await Handle(ManifestKind.Vanilla, Group(withMets: false));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
