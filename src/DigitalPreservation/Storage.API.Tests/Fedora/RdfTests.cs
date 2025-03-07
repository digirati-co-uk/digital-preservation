using Storage.API.Fedora.Http;

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
            .Be($"""
                  PREFIX ex: <http://example.com>
                  <> ex:thing "some-value" .
                  """);
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
            .Be($"""
                 PREFIX ex: <http://example.com>
                 PREFIX dc: <http://dc2.com>
                 <> ex:thing "some-value" .
                 <> dc:yyyy "some-other-value" .
                 """);
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
            .Be($"""
                 PREFIX ex: <http://example.com>
                 PREFIX dc: <http://dc2.com>
                 PREFIX fedora: <http://fedora.info/definitions/v4/repository#>
                 <> ex:thing "some-value" .
                 <> dc:yyyy "some-other-value" .
                 <> fedora:createdBy "Tom" .
                 """);
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
        
        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be($"""
                 PREFIX ex: <http://example.com>
                 PREFIX dc: <http://dc2.com>
                 PREFIX fedora: <http://fedora.info/definitions/v4/repository#>
                 <> ex:thing "some-value" .
                 <> dc:yyyy "some-other-value" .
                 <> fedora:createdBy "Tom" .
                 <> dc:zzzz "another-value" .
                 """);
    }
}