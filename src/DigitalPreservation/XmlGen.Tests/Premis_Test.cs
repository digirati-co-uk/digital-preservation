using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace XmlGen.Tests;

public class PremisTests
{
    [Fact (Skip = "Experimental")]
    public void Premis_Namespace_Handled()
    {
        // This will fail
        
        var standaloneOriginal = "Samples/standalone-premis-original.xml";
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        using XmlReader reader = XmlReader.Create(standaloneOriginal);
        var premis = (PremisComplexType) serializer.Deserialize(reader)!;
    }
    
    [Fact (Skip = "Experimental")]
    public void Premis_Namespace_Artifically_Handled()
    {
        var updatedXml = "Samples/standalone-premis-updated.xml";
        var serializer = new XmlSerializer(typeof(ObjectComplexType));
        using XmlReader reader = XmlReader.Create(updatedXml);
        var premisFile = (File) serializer.Deserialize(reader)!;
        premisFile.Should().NotBeNull();
    }
    
    
    [Fact (Skip = "Experimental")]
    public void Premis_Namespace_Added_Manually()
    {
        // This will fail
        
        var standaloneOriginal = "Samples/standalone-premis-original.xml";
        using XmlReader reader = XmlReader.Create(standaloneOriginal);
        
        XmlNamespaceManager manager = new XmlNamespaceManager(reader.NameTable);
        if (!manager.HasNamespace("premis"))
        {
            manager.AddNamespace("premis", "http://www.loc.gov/premis/v3");
            manager.PushScope();
        }
        if (!manager.HasNamespace("xsi"))
        {
            manager.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
            manager.PushScope();
        }
        var serializer = new XmlSerializer(typeof(ObjectComplexType));
        var premisFile = (File) serializer.Deserialize(reader)!;
        premisFile.Should().NotBeNull();
    }

    [Fact (Skip = "Experimental")]
    public void Premis_Namespace_Added_To_New_XmlDocument()
    {
        var doc = new XmlDocument();
        doc.Load("Samples/standalone-premis-original.xml");
        XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
        if (!manager.HasNamespace("premis"))
        {
            manager.AddNamespace("premis", "http://www.loc.gov/premis/v3");
        }
        if (!manager.HasNamespace("xsi"))
        {
            manager.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        }
        using XmlReader reader = new XmlNodeReader(doc);;
        var serializer = new XmlSerializer(typeof(ObjectComplexType));
        var premisFile = (File) serializer.Deserialize(reader)!;
        premisFile.Should().NotBeNull();
    }

    [Fact (Skip = "Experimental")]
    public void Premis_2()
    {
        var doc = new XmlDocument();
        doc.Load("Samples/standalone-premis-2.xml");
        using XmlReader reader = new XmlNodeReader(doc);;
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var premis = (PremisComplexType) serializer.Deserialize(reader)!;
        premis.Should().NotBeNull();
        
    }
    
}