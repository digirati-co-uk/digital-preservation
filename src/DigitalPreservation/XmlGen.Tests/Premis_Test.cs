using DigitalPreservation.Mets;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using FluentAssertions;
using System.Xml;
using System.Xml.Serialization;
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
        var premisManager = new PremisManager();
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        s.Should().Contain("http://www.loc.gov/premis/v3");
        s.Should().Contain(testData.Digest!);
        s.Should().Contain("fmt/999");
        s.Should().Contain("9999999");
        s.Should().Contain("files/a-file-path");
        s.Should().Contain("Tagged Image File Format Test");
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
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        var xmlElement = PremisManager.GetXmlElement(premis, false);

        testOutputHelper.WriteLine(xmlElement?.ToString());

        xmlElement.Should().NotBeNull();
        xmlElement!.NamespaceURI.Should().Be("http://www.loc.gov/premis/v3");
        xmlElement.OuterXml.Should().Contain(testData.Digest!);
        xmlElement.OuterXml.Should().Contain("fmt/999");
        xmlElement.OuterXml.Should().Contain("files/a-file-path");
    }


    [Fact]
    public void Edit_Premis_Size()
    {
        var premisManager = new PremisManager();
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        var update = new FileFormatMetadata
        {
            Source = "Tests",
            Size = 1111111
        };
        premisManager.Patch(premis, update);

        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.Size.Should().Be(1111111);
        read.Digest.Should().Be(testData.Digest);
        read.PronomKey.Should().Be("fmt/999");
        read.FormatName.Should().Be("Tagged Image File Format Test");
        read.OriginalName.Should().Be("files/a-file-path");
    }

    [Fact]
    public void Edit_Premis_PronomKey_Only()
    {
        var premisManager = new PremisManager();
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        var update = new FileFormatMetadata
        {
            Source = "Tests",
            PronomKey = "fmt/333"
        };
        premisManager.Patch(premis, update);

        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.PronomKey.Should().Be("fmt/333");
        read.FormatName.Should().Be("Tagged Image File Format Test", "patching the PRONOM key alone must not alter the format name");
        read.Digest.Should().Be(testData.Digest);
        read.Size.Should().Be(9999999);
    }

    [Fact]
    public void Edit_Premis_PronomKey_And_Format_Name()
    {
        var premisManager = new PremisManager();
        var testData = GetTestPremisData();
        var premis = premisManager.Create(testData);
        var update = new FileFormatMetadata
        {
            Source = "Tests",
            FormatName = "Some other bitmap",
            PronomKey = "fmt/333"
        };
        premisManager.Patch(premis, update);

        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.PronomKey.Should().Be("fmt/333");
        read.FormatName.Should().Be("Some other bitmap");
        read.Digest.Should().Be(testData.Digest);
        read.Size.Should().Be(9999999);
    }

    [Fact]
    public void Simplest_Premis()
    {
        var premisManager = new PremisManager();
        var premis = premisManager.Create(new FileFormatMetadata{Source = "Tests"});
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        s.Should().Contain("http://www.loc.gov/premis/v3");
        var read = premisManager.Read(premis);
        read.Should().NotBeNull();
        read!.Digest.Should().BeNull();
        read.Size.Should().BeNull();
        read.PronomKey.Should().BeNull();
        read.OriginalName.Should().BeNull();
    }
    
    
    [Fact]
    public void Premis_Digest_Only()
    {
        var premisManager = new PremisManager();
        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456"
        });
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.Digest.Should().Be("123456");
        read.Size.Should().BeNull();
        read.PronomKey.Should().BeNull();
        s.Should().Contain("123456");
        s.Should().Contain("SHA256");
    }
    
        
    [Fact]
    public void Premis_Digest_and_Size_Only()
    {
        var premisManager = new PremisManager();
        var premis = premisManager.Create(new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        });
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.Digest.Should().Be("123456");
        read.Size.Should().Be(654321);
        read.PronomKey.Should().BeNull();
    }
    
    [Fact]
    public void Premis_Digest_and_Size_then_Edit_Name()
    {
        var premisManager = new PremisManager();
        var start = new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        };
        var premis = premisManager.Create(start);
        start.OriginalName = "bob";
        premisManager.Patch(premis, start);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.Digest.Should().Be("123456");
        read.Size.Should().Be(654321);
        read.OriginalName.Should().Be("bob");
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
        var premisManager = new PremisManager();
        var premis = premisManager.Create(start);
        start.OriginalName = "bob";
        premisManager.Patch(premis, start);
        start.PronomKey = "fmt/bob";
        premisManager.Patch(premis, start);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        // Asserting on the serialised XML here: the PRONOM key was patched without a
        // format name, so the format element has a registry key but no designation.
        s.Should().Contain("123456");
        s.Should().Contain("654321");
        s.Should().Contain("bob");
        s.Should().Contain("fmt/bob");
        s.Should().Contain("PRONOM");
    }

    [Fact]
    public void Premis_Read_After_PronomKey_Only_Patch()
    {
        var start = new FileFormatMetadata
        {
            Source = "Tests",
            Digest = "123456",
            Size = 654321
        };
        var premisManager = new PremisManager();
        var premis = premisManager.Create(start);
        start.PronomKey = "fmt/bob";
        premisManager.Patch(premis, start);

        var read = premisManager.Read(premis);
        read!.Digest.Should().Be("123456");
        read.Size.Should().Be(654321);
        read.PronomKey.Should().Be("fmt/bob");
        read.FormatName.Should().BeNull("a PRONOM-key-only format has no FormatDesignation to read a name from");
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
        var premisManager = new PremisManager();
        var premis = premisManager.Create(premisMetadata);
        premisMetadata.OriginalName = "bob";
        premisManager.Patch(premis, premisMetadata);
        premisMetadata.PronomKey = "fmt/bob";
        premisManager.Patch(premis, premisMetadata);
        premisMetadata.FormatName = "Some file format";
        premisManager.Patch(premis, premisMetadata);
        var s = PremisManager.Serialise(premis);
        testOutputHelper.WriteLine(s);

        var read = premisManager.Read(premis);
        read!.Digest.Should().Be("123456");
        read.Size.Should().Be(654321);
        read.OriginalName.Should().Be("bob");
        read.PronomKey.Should().Be("fmt/bob");
        read.FormatName.Should().Be("Some file format");
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