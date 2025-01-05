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
        var writer = new StringWriter();
        serializer.Serialize(writer, mets);
        writer.Close();
        
        // assert
        writer.ToString().Should().NotBeNullOrWhiteSpace();

    }
}