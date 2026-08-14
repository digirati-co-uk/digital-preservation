using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Xml;
using System.Xml.Serialization;

namespace DigitalPreservation.Mets;

public class PremisEventManagerVirus
{
    public EventComplexType Create(VirusScanMetadata virusScanMetadata)
    {
        var eventComplexType = new EventComplexType
        {
            EventType = new StringPlusAuthority
            {
                Value = Constants.VirusCheckEventType
            },
            EventDateTime = EventDateTimeFor(virusScanMetadata)
        };

        var eventDetailInformationComplexType = new EventDetailInformationComplexType
        {
            EventDetail = virusScanMetadata.VirusDefinition
        };

        var eventOutcomeInformationComplexType = new EventOutcomeInformationComplexType
        {
            EventOutcome = new StringPlusAuthority
            {
                Value = virusScanMetadata.HasVirus ? "Fail" : "Pass"
            },
            EventOutcomeDetail = { new EventOutcomeDetailComplexType
            {
                EventOutcomeDetailNote = virusScanMetadata.VirusFound
            } }
        };

        eventComplexType.EventDetailInformation.Add(eventDetailInformationComplexType);
        eventComplexType.EventOutcomeInformation.Add(eventOutcomeInformationComplexType);

        return eventComplexType;
    }

    /// <summary>
    /// When the scan ran, as an ISO 8601 UTC instant.
    /// </summary>
    /// <remarks>
    /// This used to be <c>DateTime.Now.ToLongDateString()</c>: the time the METS was written rather
    /// than the time of the event, to a day's precision, in whatever format the host's culture
    /// happened to produce ("16 June 2026"). Since scans accumulate as separate events (#219),
    /// eventDateTime is what puts them in order and what distinguishes a genuine re-scan from the
    /// same scan recorded twice, so it has to be precise, machine-readable, and about the scan.
    /// </remarks>
    private static string EventDateTimeFor(VirusScanMetadata virusScanMetadata)
    {
        // Metadata.Timestamp carries the scan's own time (the ClamAV log's last-modified time - see
        // MetadataReader). Fall back to now only when we genuinely have nothing better, rather than
        // writing a default DateTime and claiming the scan happened in the year 1.
        var scanned = virusScanMetadata.Timestamp == default
            ? DateTime.UtcNow
            : virusScanMetadata.Timestamp;

        return XmlConvert.ToString(scanned.ToUniversalTime(), XmlDateTimeSerializationMode.Utc);
    }

    public static string Serialise(EventComplexType eventComplexType)
    {
        var serializer = new XmlSerializer(typeof(EventComplexType));
        var sw = new StringWriter();
        serializer.Serialize(sw, eventComplexType, GetXmlSerializerNameSpaces());
        return sw.ToString();
    }

    public static XmlElement? GetXmlElement(EventComplexType eventComplexType)
    {
        var serializer = new XmlSerializer(typeof(EventComplexType));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild())
        {
            serializer.Serialize(xw, eventComplexType, GetXmlSerializerNameSpaces());
        }

        return doc.DocumentElement;
    }

    private static XmlSerializerNamespaces GetXmlSerializerNameSpaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        namespaces.Add("version", "3.0");

        return namespaces;
    }
}
