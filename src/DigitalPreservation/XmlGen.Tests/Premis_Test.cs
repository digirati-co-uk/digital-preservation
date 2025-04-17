using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using Xunit.Abstractions;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace XmlGen.Tests;

public class PremisTests
{
    private readonly ITestOutputHelper testOutputHelper;

    public PremisTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    [Fact (Skip = "Experimental")]
    public void Premis_Namespace_Handled()
    {
        // This will fail
        
        var standaloneOriginal = "Samples/standalone-premis-original.xml";
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        using XmlReader reader = XmlReader.Create(standaloneOriginal);
        var premis = (PremisComplexType) serializer.Deserialize(reader)!;
    }
    
    [Fact]
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

    [Fact]
    public void Premis_2()
    {
        var doc = new XmlDocument();
        doc.Load("Samples/standalone-premis-2.xml");
        using XmlReader reader = new XmlNodeReader(doc);;
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var premis = (PremisComplexType) serializer.Deserialize(reader)!;
        premis.Should().NotBeNull();
        
    }

    [Fact]
    public void Build_Premis()
    {
        var premis = new PremisComplexType();
        var premisFile = new File();
        premis.Object.Add(premisFile);
        var objectCharacteristics = new ObjectCharacteristicsComplexType();
        premisFile.ObjectCharacteristics.Add(objectCharacteristics);
        
        var fixity = new FixityComplexType
        {
            MessageDigestAlgorithm = new MessageDigestAlgorithm{ Value = "SHA256" },
            MessageDigest = "efc63a2c4dbb61936b5028c637c76f066ce463b5de6f3d5d674c9f024fa08d73"
        };
        objectCharacteristics.Fixity.Add(fixity);

        objectCharacteristics.Size = 46857743;

        var format = new FormatComplexType
        {
            FormatDesignation = new FormatDesignationComplexType
            {
                FormatName = new FormatName { Value = "Tagged Image File Format" }
            }
        };
        var registry = new FormatRegistryComplexType
        {
            FormatRegistryName = new FormatRegistryName { Value = "PRONOM" },
            FormatRegistryKey = new FormatRegistryKey { Value = "fmt/353" }
        };
        format.FormatRegistry.Add(registry);
        objectCharacteristics.Format.Add(format);

        premisFile.OriginalName = new OriginalNameComplexType
        {
            Value = "files/the-tiff-was-here.tiff"
        };

        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var sw = new StringWriter();
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        namespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        serializer.Serialize(sw, premis, namespaces);
        var s = sw.ToString();
        testOutputHelper.WriteLine(s);
    }
    
}