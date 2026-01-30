using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using Storage.Repository.Common.Mets;
using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Mets;
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
        var premisManager = new PremisManager();
        var premisEventManager = new PremisEventManagerVirus();
        var premis = premisManager.Create(testPremisData);
        var s = premisManager.Serialise(premis);

        var premisEvent = premisEventManager.Create(testVirusMetadata);
        var s1 = premisEventManager.Serialise(premisEvent);

        testOutputHelper.WriteLine(s);
        testOutputHelper.WriteLine(s1);
    }

    [Fact]
    public void Read_Premis()
    {
        var premisManager = new PremisManager();
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        var read = premisManager.Read(premis);
        read.Should().BeEquivalentTo(testData);
    }

    [Fact]
    public void Build_Premis_Get_XmlElement()
    {
        var premisManager = new PremisManager();
        var premisEventManager = new PremisEventManagerVirus();
        var testPremisData = GetTestPremisData();
        var testVirusMetadata = GetTestVirusMetadataData();
        var premis = premisManager.Create(testPremisData);
        var s = premisManager.Serialise(premis);

        var premisEvent = premisEventManager.Create(testVirusMetadata);
        var s1 = premisEventManager.Serialise(premisEvent);

        var xmlElement = premisManager.GetXmlElement(premis, false);
        var xmlElementMetadata = premisEventManager.GetXmlElement(premisEvent);

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
