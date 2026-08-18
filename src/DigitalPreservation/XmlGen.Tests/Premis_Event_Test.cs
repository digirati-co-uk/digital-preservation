using System.Globalization;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using DigitalPreservation.Mets;
using Xunit.Abstractions;

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
        var s = PremisManager.Serialise(premis);

        var premisEvent = premisEventManager.Create(testVirusMetadata);
        var s1 = PremisEventManagerVirus.Serialise(premisEvent);

        testOutputHelper.WriteLine(s);
        testOutputHelper.WriteLine(s1);

        s.Should().Contain(testPremisData.Digest!);
        s.Should().Contain("fmt/999");
        s1.Should().Contain("virus check");
        s1.Should().Contain("Fail");
        s1.Should().Contain("EICAR found");
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
        var premisEvent = premisEventManager.Create(testVirusMetadata);

        var xmlElement = PremisManager.GetXmlElement(premis, false);
        var xmlElementMetadata = PremisEventManagerVirus.GetXmlElement(premisEvent);

        testOutputHelper.WriteLine(xmlElement?.ToString());
        testOutputHelper.WriteLine(xmlElementMetadata?.ToString());

        var serialisedPremis = PremisManager.Serialise(premis);
        serialisedPremis.Should().Contain(testPremisData.Digest!);
        var serialisedEvent = PremisEventManagerVirus.Serialise(premisEvent);
        serialisedEvent.Should().Contain("virus check");

        xmlElement.Should().NotBeNull();
        xmlElement!.NamespaceURI.Should().Be("http://www.loc.gov/premis/v3");
        xmlElement.OuterXml.Should().Contain(testPremisData.Digest!);
        xmlElementMetadata.Should().NotBeNull();
        xmlElementMetadata!.OuterXml.Should().Contain("virus check");
        xmlElementMetadata.OuterXml.Should().Contain("EICAR found");
        xmlElementMetadata.OuterXml.Should().Contain("Fail");
    }

    /// <summary>
    /// eventDateTime is the date of the EVENT. It used to be stamped at write time with
    /// DateTime.Now.ToLongDateString(), which is a different thing, to a day's precision, in the
    /// host culture's format ("16 June 2026"). Scans accumulate as separate events (#219), so this
    /// is the field that orders them and that tells a genuine re-scan from one scan recorded twice
    /// (#221) - it has to be the scan's own time, and machine-readable.
    /// </summary>
    [Fact]
    public void Virus_Event_Records_When_The_Scan_Ran_In_Iso_8601()
    {
        var scannedAt = new DateTime(2026, 6, 16, 14, 30, 45, DateTimeKind.Utc);
        var premisEvent = new PremisEventManagerVirus().Create(new VirusScanMetadata
        {
            Source = "ClamAv",
            HasVirus = false,
            Timestamp = scannedAt
        });

        premisEvent.EventDateTime.Should().Be("2026-06-16T14:30:45Z");
    }

    [Fact]
    public void Virus_Event_Date_Is_Independent_Of_The_Host_Culture()
    {
        // ToLongDateString() rendered this differently on a machine set to, say, de-DE; a stored
        // instant must not depend on where the container happened to be running.
        var scannedAt = new DateTime(2026, 6, 16, 14, 30, 45, DateTimeKind.Utc);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var premisEvent = new PremisEventManagerVirus().Create(new VirusScanMetadata
            {
                Source = "ClamAv",
                HasVirus = false,
                Timestamp = scannedAt
            });

            premisEvent.EventDateTime.Should().Be("2026-06-16T14:30:45Z");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Virus_Event_With_No_Known_Scan_Time_Falls_Back_To_Now_Not_Year_One()
    {
        // Timestamp unset. Writing default(DateTime) would claim the scan happened in the year 1.
        var premisEvent = new PremisEventManagerVirus().Create(new VirusScanMetadata
        {
            Source = "ClamAv",
            HasVirus = false
        });

        var written = DateTime.Parse(premisEvent.EventDateTime, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        written.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
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
