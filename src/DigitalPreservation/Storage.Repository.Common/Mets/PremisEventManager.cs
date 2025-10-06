using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Storage.Repository.Common.Mets;
public static class PremisEventManager
{
    private static readonly XmlSerializerNamespaces Namespaces;

    static PremisEventManager()
    {
        Namespaces = new XmlSerializerNamespaces();
        Namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        //Namespaces.Add("xsi:schemaLocation", "http://www.loc.gov/premis/v3 http://www.loc.gov/standards/premis/v3/premis.xsd");
        Namespaces.Add("version", "3.0");
    }

    public static EventComplexType Create(VirusScanMetadata virusScanMetadata)
    {
        var eventComplexType = new EventComplexType
        {
            EventType = new StringPlusAuthority
            {
                Value = "virus check"
            },
            EventDateTime = DateTime.Now.ToLongDateString() //TODO: this is a placeholder
        };

        var eventDetailInformationComplexType = new EventDetailInformationComplexType
        {
            EventDetail = "program=\"ClamAV (clamd)\"; version=\"ClamAV 1.2.2\"; virusDefinitions=\"27182/Sun Feb 11 09:33:24 2024\"" //TODO: placeholder
        };

        var eventOutcomeInformationComplexType = new EventOutcomeInformationComplexType
        {
            EventOutcome = new StringPlusAuthority
            {
                Value = "Fail"
            },
            EventOutcomeDetail = { new EventOutcomeDetailComplexType
            {
                EventOutcomeDetailNote = "/home/brian/Test/data/objects/virus_test_file.txt: Eicar-Signature FOUND"
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
        serializer.Serialize(sw, eventComplexType, Namespaces);
        return sw.ToString();
    }

    public static XmlElement? GetXmlElement(EventComplexType eventComplexType) //, bool fileElement
    {
        var serializer = new XmlSerializer(typeof(EventComplexType));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild())
        {
            serializer.Serialize(xw, eventComplexType, Namespaces);
        }
        //if (fileElement)
        //{
        //    return doc.DocumentElement?.FirstChild as XmlElement;
        //}
        return doc.DocumentElement;
    }
}
