using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Workspace;
using FluentAssertions;
using Storage.Repository.Common.Mets;
using System.Xml;


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
