using Storage.API.Fedora.Http;

namespace Storage.API.Tests.Fedora;

public class RdfTests
{
    private static string Actual(HttpRequestMessage msg) =>
        ((StringContent)msg.Content!).ReadAsStringAsync().Result
            .Replace("\r\n", "\n")
            .TrimEnd();

    [Fact] public void Single_Rdf_Statement_Request_Content()
    {
        // Arrange
        var msg = new HttpRequestMessage();
        
        // Act
        msg.AppendRdf("ex", "http://example.com", "<> ex:thing \"some-value\"");

        // Assert
        Actual(msg).Should().Be(
            """
            PREFIX ex: <http://example.com>
            <> ex:thing "some-value" .
            """.TrimEnd());
    }

    [Fact] public void Two_Rdf_Statements_Request_Content()
    {
        // Arrange
        var msg = new HttpRequestMessage();
        
        // Act
        msg.AppendRdf("ex", "http://example.com", "<> ex:thing \"some-value\"");
        msg.AppendRdf("dc", "http://dc2.com", "<> dc:yyyy \"some-other-value\"");

        // Assert
        Actual(msg).Should().Be(
            """
            PREFIX ex: <http://example.com>
            PREFIX dc: <http://dc2.com>
            <> ex:thing "some-value" .
            <> dc:yyyy "some-other-value" .
            """.TrimEnd());
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
        Actual(msg).Should().Be(
            """
            PREFIX ex: <http://example.com>
            PREFIX dc: <http://dc2.com>
            PREFIX fedora: <http://fedora.info/definitions/v4/repository#>
            <> ex:thing "some-value" .
            <> dc:yyyy "some-other-value" .
            <> fedora:createdBy "Tom" .
            """.TrimEnd());
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
        Actual(msg).Should().Be(
            """
            PREFIX ex: <http://example.com>
            PREFIX dc: <http://dc2.com>
            PREFIX fedora: <http://fedora.info/definitions/v4/repository#>
            <> ex:thing "some-value" .
            <> dc:yyyy "some-other-value" .
            <> fedora:createdBy "Tom" .
            <> dc:zzzz "another-value" .
            """.TrimEnd());
    }
}