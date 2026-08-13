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
                Value = "virus check"
            },
            EventDateTime = DateTime.Now.ToLongDateString()
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
