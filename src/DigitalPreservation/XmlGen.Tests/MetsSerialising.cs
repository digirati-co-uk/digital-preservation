using System.Text;
using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.XmlGen.Mets;
using FluentAssertions;

namespace XmlGen.Tests;

public class MetsSerialising
{
    [Fact]
    public void Can_Create_Simplest_METS()
    {
        // arrange
        var mets = new Mets
        {
            MetsHdr = new()
            {
                Createdate = DateTime.Now,
                Agent = { 
                    new MetsTypeMetsHdrAgent
                    {
                        Role = MetsTypeMetsHdrAgentRole.Creator, 
                        Type = MetsTypeMetsHdrAgentType.Other, 
                        Othertype = "SOFTWARE",
                        Name = nameof(MetsSerialising)
                    }
                }
            }
        };
        
        // act
        var serializer = new XmlSerializer(typeof(Mets));
        
        XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
        ns.Add("mets", "http://www.loc.gov/METS/");
        ns.Add("mods", "http://www.loc.gov/mods/v3");
        ns.Add("premis", "http://www.loc.gov/premis/v3");
        ns.Add("xlink", "http://www.w3.org/1999/xlink");
        ns.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        // var writer = new StringWriter();
        // serializer.Serialize(writer, mets);
        // writer.Close();
        //
        // writer.ToString().Should().NotBeNullOrWhiteSpace();

        string? xml1 = null;
        string? xml2 = null;
        
        var sb = new StringBuilder();
        var writer1 = XmlWriter.Create(sb, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
            Encoding = Encoding.UTF8,
            Indent = true,
        });
        serializer.Serialize(writer1, mets, ns);
        writer1.Close();
        xml1 = sb.ToString();
        
        //using var stream = new MemoryStream ();
        // var writer = new XmlTextWriter(stream, Encoding.UTF8);
        // serializer.Serialize(writer, mets);
        // xml = Encoding.UTF8.GetString(stream.ToArray());
        // assert
        
        using (var ms = new MemoryStream())
        using (var writer2 = new XmlTextWriter(ms, new UTF8Encoding(false)))
        {
            writer2.Formatting = Formatting.Indented;
            serializer.Serialize(writer2, mets, ns);
            xml2 = Encoding.UTF8.GetString(ms.ToArray());
        }
        
        xml1.Should().NotBeNullOrWhiteSpace();
        xml2.Should().NotBeNullOrWhiteSpace();

    }
}