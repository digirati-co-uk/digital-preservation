using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Mets;

namespace Storage.Repository.Common.Mets;

public class PremisEventManager : IPremisEventManager
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

    public void Patch(EventComplexType eventComplexType, VirusScanMetadata virusScanMetadata)
    {
        //virusScanMetadata is the update
        if (eventComplexType.EventType == null)
        {
            eventComplexType.EventType = new StringPlusAuthority
            {
                Value = "virus check"
            };

        }

        if (string.IsNullOrWhiteSpace(eventComplexType.EventDateTime))
        {
            eventComplexType.EventDateTime = DateTime.UtcNow.ToLongDateString();
        }

        if (!eventComplexType.EventDetailInformation.Any())
        {
            var eventDetailInformationComplexType = new EventDetailInformationComplexType
            {
                EventDetail = virusScanMetadata.VirusDefinition
            };

            eventComplexType.EventDetailInformation.Add(eventDetailInformationComplexType);
        }
        else
        {
            eventComplexType.EventDetailInformation[0].EventDetail = virusScanMetadata.VirusDefinition;
        }


        if (!eventComplexType.EventOutcomeInformation.Any())
        {
            var eventOutcomeInformationComplexType = new EventOutcomeInformationComplexType
            {
                EventOutcome = new StringPlusAuthority
                {
                    Value = virusScanMetadata.HasVirus ? "Fail" : "Success"
                },
                EventOutcomeDetail = { new EventOutcomeDetailComplexType
                {
                    EventOutcomeDetailNote = virusScanMetadata.VirusFound
                } }
            };

            eventComplexType.EventOutcomeInformation.Add(eventOutcomeInformationComplexType);
        }
    }

    public string Serialise(EventComplexType eventComplexType)
    {
        var serializer = new XmlSerializer(typeof(EventComplexType));
        var sw = new StringWriter();
        serializer.Serialize(sw, eventComplexType, GetXmlSerializerNameSpaces());
        return sw.ToString();
    }

    public XmlElement? GetXmlElement(EventComplexType eventComplexType)
    {
        var serializer = new XmlSerializer(typeof(EventComplexType));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild())
        {
            serializer.Serialize(xw, eventComplexType, GetXmlSerializerNameSpaces());
        }

        return doc.DocumentElement;
    }

    private XmlSerializerNamespaces GetXmlSerializerNameSpaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        namespaces.Add("version", "3.0");

        return namespaces;
    }
}
