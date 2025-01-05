
using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.XmlGen.Mods.V3;
using DigitalPreservation.XmlGen.Premis.V3;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace DigitalPreservation.XmlGen.Extensions;

public static class XElementX
{
    public static ModsDefinition? ToMods(this XmlElement rawMods)
    {
        var serializer = new XmlSerializer(typeof(ModsDefinition));
        using var xmlNodeReader = new XmlNodeReader(rawMods);
        return (ModsDefinition?)serializer.Deserialize(xmlNodeReader);
    }
    
    /// <summary>
    /// The XML data might be a premis:premis, or a premis:object
    /// </summary>
    /// <param name="rawPremisXml"></param>
    /// <returns></returns>
    public static PremisComplexType? GetPremisComplexObject(this XmlElement rawPremisXml)
    {
        // I think that Goobi's example is incompatible with the generated classes.
        // We need a `PremisComplexType` to deserialise, rather than ObjectComplexType
        
        // so examine the premisXml and manipulate it into a form acceptable to the serializer
        XmlNode nodeToRead;
        if (rawPremisXml.Name != "premis:premis")
        {
            const string xml = @"<premis:premis version=""3.0""
               xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
               xmlns:premis=""http://www.loc.gov/premis/v3""
               xsi:schemaLocation=""http://www.loc.gov/premis/v3 http://www.loc.gov/standards/premis/v3/premis.xsd""
></premis:premis>";
            var premisDoc = new XmlDocument();
            premisDoc.LoadXml(xml);
            rawPremisXml.RemoveAttribute("version");
            rawPremisXml.RemoveAttribute("xsi:schemaLocation");
            var importNode = premisDoc.ImportNode(rawPremisXml, true);
            premisDoc.DocumentElement!.AppendChild(importNode);
            nodeToRead = premisDoc;
        }
        else
        {
            nodeToRead = rawPremisXml;
        }
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        using var xmlNodeReader = new XmlNodeReader(nodeToRead);
        var des = serializer.Deserialize(xmlNodeReader);
        return des as PremisComplexType;

    }
    
}