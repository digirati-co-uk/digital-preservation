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

    [Fact]
    public void Replace_Title_Deletes_Every_Existing_Title_And_Inserts_The_New_One()
    {
        // Issue #347, decision (a): a rename replaces dc:title, so a resource has one title.
        var msg = new HttpRequestMessage();

        msg.AsReplaceTitlePatch("New \"name\"", "tester");

        Lines(msg).Select(l => l.Trim()).Should().Equal(
            "PREFIX dc: <http://purl.org/dc/elements/1.1/>",
            "PREFIX fedora: <http://fedora.info/definitions/v4/repository#>",
            "DELETE {",
            "<> dc:title ?title .",
            "}",
            "INSERT {",
            "<> dc:title \"New \\\"name\\\"\" .",
            "<> fedora:lastModifiedBy \"tester\" .",
            "}",
            "WHERE { OPTIONAL { <> dc:title ?title } }");
        msg.Content!.Headers.ContentType!.MediaType.Should().Be("application/sparql-update");
    }

    [Fact]
    public void Container_Metadata_Update_With_A_Name_Replaces_The_Title()
    {
        // Previously an INSERT with no DELETE, so Archival Groups accumulated titles.
        var msg = new HttpRequestMessage();

        msg.WithContainerMetadataUpdate("Renamed group", "tester");

        var lines = Lines(msg).Select(l => l.Trim()).ToList();
        lines.Should().Contain("<> dc:title ?title .");
        lines.Should().Contain("<> dc:title \"Renamed group\" .");
        lines.Should().Contain("WHERE { OPTIONAL { <> dc:title ?title } }");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Container_Metadata_Update_Without_A_Name_Leaves_The_Title_Alone(string? name)
    {
        var msg = new HttpRequestMessage();

        msg.WithContainerMetadataUpdate(name, "tester");

        var body = ((StringContent)msg.Content!).ReadAsStringAsync().Result;
        body.Should().NotContain("dc:title");
        body.Should().NotContain("DELETE");
        body.Should().Contain("fedora:lastModifiedBy \"tester\"");
    }
}
