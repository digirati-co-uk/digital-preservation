using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using Storage.Repository.Common.Mets;
using System.Xml;
using System.Xml.Serialization;
using Xunit.Abstractions;
using Xunit.Sdk;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace XmlGen.Tests;
public class Premis_Event_Test(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public void Build_Premis_And_Virus_Metadata()
    {
        var testPremisData = GetTestPremisData();
        var testVirusMetadata = GetTestVirusMetadataData();
        var premis = PremisManager.Create(testPremisData);
        var s = PremisManager.Serialise(premis);

        var premisEvent = PremisEventManager.Create(testVirusMetadata);
        var s1 = PremisEventManager.Serialise(premisEvent);

        testOutputHelper.WriteLine(s);
        testOutputHelper.WriteLine(s1);
    }

    [Fact]
    public void Read_Premis()
    {
        var testData = GetTestPremisData();
        var premis = PremisManager.Create(testData);
        var read = PremisManager.Read(premis);
        read.Should().BeEquivalentTo(testData);
    }

    [Fact]
    public void Build_Premis_Get_XmlElement()
    {
        var testPremisData = GetTestPremisData();
        var testVirusMetadata = GetTestVirusMetadataData();
        var premis = PremisManager.Create(testPremisData);
        var s = PremisManager.Serialise(premis);

        var premisEvent = PremisEventManager.Create(testVirusMetadata);
        var s1 = PremisEventManager.Serialise(premisEvent);

        var xmlElement = PremisManager.GetXmlElement(premis, false);
        var xmlElementMetadata = PremisEventManager.GetXmlElement(premisEvent);

        testOutputHelper.WriteLine(xmlElement?.ToString());
        testOutputHelper.WriteLine(xmlElementMetadata?.ToString());
    }

    private static VirusScanMetadata GetTestVirusMetadataData()
    {
        var testData = new VirusScanMetadata
        {
            Source = "ClamAv",
            VirusFound = "EICAR found",
            HasVirus = true,
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
}
