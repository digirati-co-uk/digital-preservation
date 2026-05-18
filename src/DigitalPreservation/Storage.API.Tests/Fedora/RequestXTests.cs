using Storage.API.Fedora.Http;

namespace Storage.API.Tests.Fedora;

public class RequestXTests
{
    private static string[] Lines(HttpRequestMessage msg) =>
        ((StringContent)msg.Content!).ReadAsStringAsync().Result
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);

    private static string[] Expected(string s) =>
        s.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Single_Rdf_Statement_Simple_Name()
    {
        // Arrange
        var msg = new HttpRequestMessage();

        // Act
        msg.WithName("Simple name");
        
        // Assert
        Lines(msg).Should().Equal(Expected("""
            PREFIX dc: <http://purl.org/dc/elements/1.1/>
            <> dc:title "Simple name" .
            """));
    }

    [Fact]
    public void Single_Rdf_Statement_Dodgy_Name()
    {
        // Arrange
        var msg = new HttpRequestMessage();

        // Act
        msg.WithName("Barth Bridge. Original drawing used in \"The Yorkshire Dales\" (1956), page 156");

        // Assert
        Lines(msg).Should().Equal(Expected("""
            PREFIX dc: <http://purl.org/dc/elements/1.1/>
            <> dc:title "Barth Bridge. Original drawing used in \"The Yorkshire Dales\" (1956), page 156" .
            """));
    }
}