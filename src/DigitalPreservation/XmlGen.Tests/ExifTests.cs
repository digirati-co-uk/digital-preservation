using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Workspace;
using FluentAssertions;
using DigitalPreservation.Mets;
using System.Xml;
using VFile = DigitalPreservation.XmlGen.Premis.V3.File;


namespace XmlGen.Tests;

public class ExifTests()
{
    [Fact]
    private void Build_Exif()
    {
        var premisManager = new PremisManager();
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        //patch the Premis with exif
        var premisExifManager = new PremisManagerExif();
        var testDataExif = GetTestExifData();
        premisExifManager.Patch(premis, testDataExif);

        var xmlElement = premisExifManager.GetXmlElement(premis, false);

        var exifMetadataNodeList = xmlElement?.SelectNodes("//*[name()='ExifMetadata']");

        exifMetadataNodeList.Should().NotBeNull();

        if (exifMetadataNodeList != null)
        {
            foreach (XmlNode node in exifMetadataNodeList)
            {
                var exifToolVersion = node.SelectNodes("//*[name()='ExifToolVersion']");
                var exifContentType = node.SelectNodes("//*[name()='ContentType']");

                exifToolVersion?[0]?.InnerText.Should().Be("1.3.4");
                exifContentType?[0]?.InnerText.Should().Be("text/plain");
            }
        }
    }

    [Fact]
    private static void Can_Get_Metadata_Html()
    {
        var exifFi = new FileInfo("Samples/exif_output.txt");
        var exifResultStr = string.Empty;
        using (StreamReader sr = new StreamReader(exifFi.FullName))
        {
            exifResultStr += sr.ReadToEnd();
        }

        var exifMetadataList = MetadataReader.ParseExifToolOutput(exifResultStr);

        var exifModel = exifMetadataList.FirstOrDefault();

        //model contains RawOutput
        exifModel?.ExifMetadata.LastOrDefault()?.TagName.Should().Be("RawOutput");
        var metadataList = GetMetadataList();

        metadataList.Should().HaveCount(3);

        MetadataReader.SetMetadataHtml(metadataList, exifModel, DateTime.Now);

        //tool output added
        metadataList.Should().HaveCount(4);
        var metadataType = metadataList[3].GetType();

        metadataType.Name.Should().Be("ToolOutput");
        var toolOutput = (ToolOutput)metadataList[3];

        toolOutput.Content.Should().Contain("<h1>File format</h1>");
        toolOutput.Content.Should().Contain("<h1>Viruses</h1>");
        toolOutput.Content.Should().Contain("<h1>Exif</h1>");

    } 

    [Fact]
    private void Patch_VideoFile_ProducesUniqueSignificantProperties()
    {
        var premisManager = new PremisManager();
        var premisExifManager = new PremisManagerExif();

        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Brunnhilde",
            Digest = "b2acb9bddcb384f9762f919af8f6d8b4be781e40ad2f35a37cc12beef55b9a27",
            Size = 249229883,
            FormatName = "Quicktime",
            PronomKey = "x-fmt/384",
            OriginalName = "objects/big_buck_bunny_480p_h264.mov"
        });

        // Real-world tag sequence from big_buck_bunny_480p_h264.mov. The MOV container has
        // multiple tracks — ImageWidth/ImageHeight appear once per track (video: 853x480,
        // timecode: 853x20). ImageSize is ExifTool's composite tag representing the overall
        // video frame dimensions and must win over the per-track values.
        premisExifManager.Patch(premis, new ExifMetadata
        {
            Source = "Exif",
            Tags =
            [
                new() { TagName = "ImageWidth",  TagValue = "853" },
                new() { TagName = "ImageHeight", TagValue = "480" },
                new() { TagName = "Duration",    TagValue = "0:09:56" },
                new() { TagName = "ImageWidth",  TagValue = "853" },   // timecode track
                new() { TagName = "ImageHeight", TagValue = "20" },    // timecode track — misleading
                new() { TagName = "AvgBitrate",  TagValue = "3.34 Mbps" },
                new() { TagName = "ImageSize",   TagValue = "853x480" },
            ]
        });

        var file = premis.Object.OfType<VFile>().Single();
        var props = file.SignificantProperties;

        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageWidth")
            .Which.SignificantPropertiesValue.Single().Should().Be("853");
        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageHeight")
            .Which.SignificantPropertiesValue.Single().Should().Be("480");
        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "Duration")
            .Which.SignificantPropertiesValue.Single().Should().Be("0:09:56");
        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "Bitrate")
            .Which.SignificantPropertiesValue.Single().Should().Be("3.34 Mbps");
    }

    [Fact]
    private void PatchExtent_BeforePatch_MergesWithoutConflict()
    {
        var premisManager = new PremisManager();
        var premisExifManager = new PremisManagerExif();

        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Brunnhilde",
            Digest = "b2acb9bddcb384f9762f919af8f6d8b4be781e40ad2f35a37cc12beef55b9a27",
            Size = 249229883,
            FormatName = "Quicktime",
            PronomKey = "x-fmt/384",
            OriginalName = "objects/big_buck_bunny_480p_h264.mov"
        });

        // PatchExtent sets properties first from an explicit source
        premisExifManager.PatchExtent(premis, new ExtentMetadata
        {
            Source = "ExtentMetadata",
            PixelWidth = 853,
            PixelHeight = 480,
        });

        // Patch from exif should agree — no conflict, no duplicates
        premisExifManager.Patch(premis, new ExifMetadata
        {
            Source = "Exif",
            Tags =
            [
                new() { TagName = "ImageWidth",  TagValue = "853" },
                new() { TagName = "ImageHeight", TagValue = "20" },    // timecode track — overridden by ImageSize
                new() { TagName = "Duration",    TagValue = "0:09:56" },
                new() { TagName = "AvgBitrate",  TagValue = "3.34 Mbps" },
                new() { TagName = "ImageSize",   TagValue = "853x480" },
            ]
        });

        var file = premis.Object.OfType<VFile>().Single();
        var props = file.SignificantProperties;

        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageWidth")
            .Which.SignificantPropertiesValue.Single().Should().Be("853");
        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageHeight")
            .Which.SignificantPropertiesValue.Single().Should().Be("480");
    }

    [Fact]
    private void PatchExtent_AfterPatch_MergesWithoutConflict()
    {
        var premisManager = new PremisManager();
        var premisExifManager = new PremisManagerExif();

        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Brunnhilde",
            Digest = "b2acb9bddcb384f9762f919af8f6d8b4be781e40ad2f35a37cc12beef55b9a27",
            Size = 249229883,
            FormatName = "Quicktime",
            PronomKey = "x-fmt/384",
            OriginalName = "objects/big_buck_bunny_480p_h264.mov"
        });

        premisExifManager.Patch(premis, new ExifMetadata
        {
            Source = "Exif",
            Tags =
            [
                new() { TagName = "ImageWidth",  TagValue = "853" },
                new() { TagName = "ImageHeight", TagValue = "20" },    // timecode track — overridden by ImageSize
                new() { TagName = "Duration",    TagValue = "0:09:56" },
                new() { TagName = "AvgBitrate",  TagValue = "3.34 Mbps" },
                new() { TagName = "ImageSize",   TagValue = "853x480" },
            ]
        });

        // PatchExtent with values that agree with exif — should merge cleanly
        premisExifManager.PatchExtent(premis, new ExtentMetadata
        {
            Source = "ExtentMetadata",
            PixelWidth = 853,
            PixelHeight = 480,
        });

        var file = premis.Object.OfType<VFile>().Single();
        var props = file.SignificantProperties;

        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageWidth")
            .Which.SignificantPropertiesValue.Single().Should().Be("853");
        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageHeight")
            .Which.SignificantPropertiesValue.Single().Should().Be("480");
    }

    [Fact]
    private void ConflictingSignificantProperties_ThrowsMetadataException()
    {
        var premisManager = new PremisManager();
        var premisExifManager = new PremisManagerExif();

        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Brunnhilde",
            Digest = "b2acb9bddcb384f9762f919af8f6d8b4be781e40ad2f35a37cc12beef55b9a27",
            Size = 249229883,
            FormatName = "Quicktime",
            PronomKey = "x-fmt/384",
            OriginalName = "objects/big_buck_bunny_480p_h264.mov"
        });

        // PatchExtent claims a different width to what exif reports
        premisExifManager.PatchExtent(premis, new ExtentMetadata
        {
            Source = "ExtentMetadata",
            PixelWidth = 800,
            PixelHeight = 480,
        });

        var act = () => premisExifManager.Patch(premis, new ExifMetadata
        {
            Source = "Exif",
            Tags = [ new() { TagName = "ImageSize", TagValue = "853x480" } ]
        });

        act.Should().Throw<MetadataException>()
            .WithMessage("*ImageWidth*800*853*");
    }

    [Fact]
    private void Patch_WithoutImageSize_FallsBackToDimensionTags()
    {
        var premisManager = new PremisManager();
        var premisExifManager = new PremisManagerExif();

        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Brunnhilde",
            Digest = "abc123",
            Size = 12345,
            FormatName = "JPEG",
            PronomKey = "fmt/44",
            OriginalName = "objects/photo.jpg"
        });

        // Static image: no ImageSize composite tag, no SourceImageWidth/Height — plain tags only
        premisExifManager.Patch(premis, new ExifMetadata
        {
            Source = "Exif",
            Tags =
            [
                new() { TagName = "ImageWidth",  TagValue = "1920" },
                new() { TagName = "ImageHeight", TagValue = "1080" },
            ]
        });

        var file = premis.Object.OfType<VFile>().Single();
        var props = file.SignificantProperties;

        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageWidth")
            .Which.SignificantPropertiesValue.Single().Should().Be("1920");
        props.Should().ContainSingle(sp => sp.SignificantPropertiesType!.Value == "ImageHeight")
            .Which.SignificantPropertiesValue.Single().Should().Be("1080");
    }

    private static ExifMetadata GetTestExifData()
    {
        var testData = new ExifMetadata
        {
            Source = "Exif",
            Tags =
            [
                new() { TagName = "ExifToolVersion", TagValue = "1.3.4" },
                new() { TagName = "ContentType", TagValue = "text/plain" }
            ],
            Timestamp = DateTime.Now
        };
        return testData;
    }

    private static FileFormatMetadata GetTestPremisData()
    {
        var testData = new FileFormatMetadata
        {
            Source = "METS",
            Digest = "efc63a2c4dbb61936b5028c637c76f066ce463b5de6f3d5d674c9f024fa08d79",
            Size = 9999999,
            FormatName = "Tagged Image File Format Test",
            PronomKey = "fmt/999",
            OriginalName = "files/a-file-path"
        };
        return testData;
    }

    private static List<Metadata> GetMetadataList()
    {
        return
        [
            new FileFormatMetadata
            {
                Source = "Brunnhilde",
                Digest = "b42a6e9c",
                ContentType = "text/plain",
                FormatName = "Text File",
                PronomKey = "fmt/101",
                Size = 9999
            },
            new VirusScanMetadata
            {
                Source = "ClamAv",
                HasVirus = true,
                VirusDefinition = "1.3.4",
                VirusFound = "EICAR-HDB",
                Timestamp = DateTime.Now
            },
            new ExifMetadata
            {
                Source = "Exif",
                Tags =
                [
                    new()
                    {
                        TagName = "ExifToolVersion",
                        TagValue = "1.3.4"
                    },

                    new()
                    {
                        TagName = "ContentType",
                        TagValue = "text/plain"
                    },
                    new()
                    {
                        TagName = "ContentType",
                        TagValue = "text/plain"
                    },
                ]
            }
        ];
    }
}
