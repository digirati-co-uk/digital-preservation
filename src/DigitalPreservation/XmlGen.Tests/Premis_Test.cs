using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using Storage.Repository.Common.Mets;
using Xunit.Abstractions;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace XmlGen.Tests;

public class PremisTests(ITestOutputHelper testOutputHelper)
{
    [Fact (Skip = "Experimental")]
    public void Premis_Namespace_Handled()
    {
        // This will fail
        
        var standaloneOriginal = "Samples/standalone-premis-original.xml";
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        using var reader = XmlReader.Create(standaloneOriginal);
        var premis = (PremisComplexType?) serializer.Deserialize(reader);
        premis.Should().NotBeNull();
    }
    
    [Fact]
    public void Premis_Namespace_Artificially_Handled()
    {
        var updatedXml = "Samples/standalone-premis-updated.xml";
        var serializer = new XmlSerializer(typeof(ObjectComplexType));
        using XmlReader reader = XmlReader.Create(updatedXml);
        var premisMetadata = (File) serializer.Deserialize(reader)!;
        premisMetadata.Should().NotBeNull();
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
        var premisMetadata = (File) serializer.Deserialize(reader)!;
        premisMetadata.Should().NotBeNull();
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
        using XmlReader reader = new XmlNodeReader(doc);
        var serializer = new XmlSerializer(typeof(ObjectComplexType));
        var premisMetadata = (File) serializer.Deserialize(reader)!;
        premisMetadata.Should().NotBeNull();
    }

    [Fact]
    public void Premis_2()
    {
        var doc = new XmlDocument();
        doc.Load("Samples/standalone-premis-2.xml");
        using XmlReader reader = new XmlNodeReader(doc);
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var premis = (PremisComplexType) serializer.Deserialize(reader)!;
        premis.Should().NotBeNull();
        
    }

    [Fact]
    public void Build_Premis()
    {
        var testData = GetTestPremisData();
        var premis = PremisManager.Create(testData);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
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
        var testData = GetTestPremisData();
        var premis = PremisManager.Create(testData);
        var xmlElement = PremisManager.GetXmlElement(premis, false);
        
        testOutputHelper.WriteLine(xmlElement?.ToString());
    }


    [Fact]
    public void Edit_Premis_Size()
    {
        var testData = GetTestPremisData();
        var premis = PremisManager.Create(testData);
        var update = new FileFormatMetadata
        {
            Source = "Tests",
            Size = 1111111
        };
        PremisManager.Patch(premis, update);
        
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
    [Fact]
    public void Edit_Premis_PronomKey_Only()
    {
        var testData = GetTestPremisData();
        var premis = PremisManager.Create(testData);
        var update = new FileFormatMetadata
        {
            Source = "Tests",
            PronomKey = "fmt/333"
        };
        PremisManager.Patch(premis, update);
        
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
    [Fact]
    public void Edit_Premis_PronomKey_And_Format_Name()
    {
        var testData = GetTestPremisData();
        var premis = PremisManager.Create(testData);
        var update = new FileFormatMetadata
        {
            Source = "Tests",
            FormatName = "Some other bitmap",
            PronomKey = "fmt/333"
        };
        PremisManager.Patch(premis, update);
        
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
    [Fact]
    public void Simplest_Premis()
    {
        var premis = PremisManager.Create(new FileFormatMetadata{Source = "Tests"});
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
    
    [Fact]
    public void Premis_Digest_Only()
    {
        var premis = PremisManager.Create(new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456"
        });
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
        
    [Fact]
    public void Premis_Digest_and_Size_Only()
    {
        var premis = PremisManager.Create(new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        });
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }        
    
    [Fact]
    public void Premis_Digest_and_Size_then_Edit_Name()
    {
        var start = new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        };
        var premis = PremisManager.Create(start);
        start.OriginalName = "bob";
        PremisManager.Patch(premis, start);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
        
    [Fact]
    public void Premis_Digest_and_Size_then_Edit_More()
    {
        var start = new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        };
        var premis = PremisManager.Create(start);
        start.OriginalName = "bob";
        PremisManager.Patch(premis, start);
        start.PronomKey = "fmt/bob";
        PremisManager.Patch(premis, start);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
    }
    
    [Fact]
    public void Premis_Build_up_all()
    {
        var premisMetadata = new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        };
        var premis = PremisManager.Create(premisMetadata);
        premisMetadata.OriginalName = "bob";
        PremisManager.Patch(premis, premisMetadata);
        premisMetadata.PronomKey = "fmt/bob";
        PremisManager.Patch(premis, premisMetadata);
        premisMetadata.FormatName = "Some file format";
        PremisManager.Patch(premis, premisMetadata);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);
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