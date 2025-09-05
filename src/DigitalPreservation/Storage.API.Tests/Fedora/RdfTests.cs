using Storage.API.Fedora.Http;
using Storage.API.Fedora.Vocab;
using static System.Net.WebRequestMethods;

namespace Storage.API.Tests.Fedora;

public class RdfTests
{
    [Fact] public void Single_Rdf_Statement_Request_Content()
    {
        // Arrange
        var msg = new HttpRequestMessage();
        
        // Act
        msg.AppendRdf("ex", "http://example.com", "<> ex:thing \"some-value\"");

        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be("PREFIX ex: <http://example.com>\r\n<> ex:thing \"some-value\" .");
    }
    
    
    [Fact] public void Two_Rdf_Statements_Request_Content()
    {
        // Arrange
        var msg = new HttpRequestMessage();
        
        // Act
        msg.AppendRdf("ex", "http://example.com", "<> ex:thing \"some-value\"");
        msg.AppendRdf("dc", "http://dc2.com", "<> dc:yyyy \"some-other-value\"");

        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be("PREFIX ex: <http://example.com>\r\nPREFIX dc: <http://dc2.com>\r\n<> ex:thing \"some-value\" .\r\n<> dc:yyyy \"some-other-value\" .");
    }    
    
    [Fact] public void Three_Rdf_Statements_Request_Content()
    {
        // Arrange
        var msg = new HttpRequestMessage();
        
        // Act
        msg.AppendRdf("ex", "http://example.com", "<> ex:thing \"some-value\"");
        msg.AppendRdf("dc", "http://dc2.com", "<> dc:yyyy \"some-other-value\"");
        msg.AppendRdf("fedora", "http://fedora.info/definitions/v4/repository#", "<> fedora:createdBy \"Tom\"");

        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be("PREFIX ex: <http://example.com>\r\nPREFIX dc: <http://dc2.com>\r\nPREFIX fedora: <http://fedora.info/definitions/v4/repository#>\r\n<> ex:thing \"some-value\" .\r\n<> dc:yyyy \"some-other-value\" .\r\n<> fedora:createdBy \"Tom\" .");
    }
    
    [Fact] public void Duplicate_Prefix_Request_Content()
    {
        // Arrange
        var msg = new HttpRequestMessage();
        
        // Act
        msg.AppendRdf("ex", "http://example.com", "<> ex:thing \"some-value\"");
        msg.AppendRdf("dc", "http://dc2.com", "<> dc:yyyy \"some-other-value\"");
        msg.AppendRdf("fedora", "http://fedora.info/definitions/v4/repository#", "<> fedora:createdBy \"Tom\"");
        msg.AppendRdf("dc", "http://dc2.com", "<> dc:zzzz \"another-value\"");


        var strContent = msg.Content as StringContent;
        var rdfString = strContent?.ReadAsStringAsync().Result;


        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be("PREFIX ex: <http://example.com>\r\nPREFIX dc: <http://dc2.com>\r\nPREFIX fedora: <http://fedora.info/definitions/v4/repository#>\r\n<> ex:thing \"some-value\" .\r\n<> dc:yyyy \"some-other-value\" .\r\n<> fedora:createdBy \"Tom\" .\r\n<> dc:zzzz \"another-value\" .");
    }
}