using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Storage.API.Fedora.Http;
using Storage.API.Tests.TestingInfrastructure;

namespace Storage.API.Tests.Fedora;

/// <summary>
/// AsReplaceTitlePatch against a real Fedora (issue #347, decision (a): a rename replaces the
/// title). The first version passed RequestXTests, which only check the SPARQL text, yet on Fedora
/// 6.5.1 it inserted the new title and kept the old one: Fedora did not apply a DELETE whose
/// variable was bound through OPTIONAL. An Archival Group renamed by an import job then reported
/// its old name in otherNames (digital-preservation-e2e-tests#131). These pin what Fedora stores.
/// </summary>
[Collection(FedoraCollection.CollectionName)]
[Trait("Category", "Integration")]
public partial class ReplaceTitleAgainstFedoraTests(FedoraFixture fedora)
{
    private const string Title = "http://purl.org/dc/elements/1.1/title";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Uri NewResourceUri() => new(fedora.Root, "replace-title-" + Guid.NewGuid().ToString("N"));

    private async Task CreateContainer(Uri uri, params string[] titles)
    {
        var triples = titles.Length == 0
            ? "<> <http://example.org/note> \"no title\" ."
            : "<> <" + Title + "> " + string.Join(", ", titles.Select(t => "\"" + t + "\"")) + " .";
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        request.Content = new StringContent(triples, Encoding.UTF8, "text/turtle");
        request.Headers.Add("Link", "<http://www.w3.org/ns/ldp#BasicContainer>;rel=\"type\"");
        using var response = await fedora.Client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpStatusCode> SendReplace(Uri uri, string title, string? transaction = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri).AsReplaceTitlePatch(title, "tester");
        if (transaction != null) request.Headers.Add("Atomic-ID", transaction);
        using var response = await fedora.Client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private async Task<List<string>> Titles(Uri uri, string? transaction = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("Accept", "application/n-triples");
        if (transaction != null) request.Headers.Add("Atomic-ID", transaction);
        using var response = await fedora.Client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return TitleTriple().Matches(body).Select(m => m.Groups[1].Value).ToList();
    }

    [GeneratedRegex("<http://purl\\.org/dc/elements/1\\.1/title> \"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex TitleTriple();

    [Fact]
    public async Task A_Container_With_One_Title_Ends_Up_With_Only_The_New_One()
    {
        var uri = NewResourceUri();
        await CreateContainer(uri, "Old name");

        (await SendReplace(uri, "New name")).Should().Be(HttpStatusCode.NoContent);

        (await Titles(uri)).Should().Equal("New name");
    }

    [Fact]
    public async Task A_Container_That_Accumulated_Titles_Ends_Up_With_Only_The_New_One()
    {
        // Archival Groups gained a title per update before #347.
        var uri = NewResourceUri();
        await CreateContainer(uri, "Old name", "Older name");

        (await SendReplace(uri, "New name")).Should().Be(HttpStatusCode.NoContent);

        (await Titles(uri)).Should().Equal("New name");
    }

    [Fact]
    public async Task A_Container_With_No_Title_Gets_The_New_One()
    {
        var uri = NewResourceUri();
        await CreateContainer(uri);

        (await SendReplace(uri, "New name")).Should().Be(HttpStatusCode.NoContent);

        (await Titles(uri)).Should().Equal("New name");
    }

    [Fact]
    public async Task Inside_A_Transaction_The_Old_Title_Is_Replaced_Too()
    {
        // The import job renames inside a Fedora transaction.
        var uri = NewResourceUri();
        await CreateContainer(uri, "Old name");
        using var begin = await fedora.Client.PostAsync(new Uri(fedora.Root + "fcr:tx"), null, Ct);
        begin.StatusCode.Should().Be(HttpStatusCode.Created);
        var transaction = begin.Headers.Location!.ToString();

        (await SendReplace(uri, "New name", transaction)).Should().Be(HttpStatusCode.NoContent);
        (await Titles(uri, transaction)).Should().Equal("New name");

        using var commit = await fedora.Client.PutAsync(transaction, null, Ct);
        commit.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Titles(uri)).Should().Equal("New name");
    }

    [Fact]
    public async Task A_Binary_Renamed_Through_Its_Metadata_Ends_Up_With_Only_The_New_Title()
    {
        var uri = NewResourceUri();
        using (var put = await fedora.Client.PutAsync(uri, new StringContent("hello", Encoding.UTF8, "text/plain"), Ct))
        {
            put.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        var metadata = new Uri(uri + "/fcr:metadata");
        await SendReplace(metadata, "Old name");
        (await Titles(metadata)).Should().Equal("Old name");

        (await SendReplace(metadata, "New name")).Should().Be(HttpStatusCode.NoContent);

        (await Titles(metadata)).Should().Equal("New name");
    }
}
