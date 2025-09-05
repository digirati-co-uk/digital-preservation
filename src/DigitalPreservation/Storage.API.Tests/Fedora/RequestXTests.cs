using Storage.API.Fedora.Http;

namespace Storage.API.Tests.Fedora;

public class RequestXTests
{
    [Fact]
    public void Single_Rdf_Statement_Simple_Name()
    {
        // Arrange
        var msg = new HttpRequestMessage();

        // Act
        msg.WithName("Simple name");
        
        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be("""
                        PREFIX dc: <http://purl.org/dc/elements/1.1/>
                        <> dc:title "Simple name" .
                        """);
    }

    [Fact]
    public void Single_Rdf_Statement_Dodgy_Name()
    {
        // Arrange
        var msg = new HttpRequestMessage();

        // Act
        msg.WithName("Barth Bridge. Original drawing used in \"The Yorkshire Dales\" (1956), page 156");

        // Assert
        ((StringContent)msg.Content!).ReadAsStringAsync().Result.Should()
            .Be("""
                PREFIX dc: <http://purl.org/dc/elements/1.1/>
                <> dc:title "Barth Bridge. Original drawing used in \"The Yorkshire Dales\" (1956), page 156" .
                """);
    }
}