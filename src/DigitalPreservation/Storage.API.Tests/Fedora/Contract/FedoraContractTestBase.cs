using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Storage.API.Fedora.Http;
using Storage.API.Fedora.Model;
using Storage.API.Fedora.Vocab;
using Storage.API.Tests.TestingInfrastructure;

namespace Storage.API.Tests.Fedora.Contract;

/// <summary>
/// Raw HTTP against a real Fedora, for the contract tests: what Fedora itself does, independent of
/// how FedoraClient happens to call it. Each test works under its own new path, so tests in one
/// collection don't see each other's resources.
/// </summary>
public abstract partial class FedoraContractTestBase(FedoraFixture fedora)
{
    protected const string Title = "http://purl.org/dc/elements/1.1/title";

    protected FedoraFixture Fedora => fedora;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string NewPath(string what) => $"contract-{what}-{Guid.NewGuid():N}";

    protected Uri At(string path) => new(fedora.Root + path);

    protected static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    protected static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>POST /fcr:tx. Returns the transaction's URI, as the Atomic-ID header carries it.</summary>
    protected async Task<string> BeginTransaction()
    {
        using var response = await fedora.Client.PostAsync(new Uri(fedora.Root + "fcr:tx"), null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, "Fedora opens a transaction");
        return response.Headers.Location!.ToString();
    }

    protected async Task<HttpResponseMessage> OnTransaction(HttpMethod method, string transaction) =>
        await fedora.Client.SendAsync(new HttpRequestMessage(method, transaction), Ct);

    protected async Task<HttpStatusCode> Commit(string transaction)
    {
        using var response = await OnTransaction(HttpMethod.Put, transaction);
        return response.StatusCode;
    }

    protected async Task<HttpResponseMessage> Send(HttpRequestMessage request, string? transaction)
    {
        if (transaction != null)
        {
            request.Headers.Add(Transaction.HeaderName, transaction);
        }
        return await fedora.Client.SendAsync(request, Ct);
    }

    protected async Task<HttpStatusCode> PutContainer(string path, string? transaction = null,
        bool archivalGroup = false, string? title = null)
    {
        var turtle = title == null ? "" : $"<> <{Title}> \"{title}\" .";
        var request = new HttpRequestMessage(HttpMethod.Put, At(path))
        {
            Content = new StringContent(turtle, Encoding.UTF8, "text/turtle")
        };
        var type = archivalGroup ? RepositoryTypes.ArchivalGroup : RepositoryTypes.BasicContainer;
        request.Headers.Add("Link", $"<{type}>;rel=\"type\"");
        using var response = await Send(request, transaction);
        return response.StatusCode;
    }

    /// <summary>A binary PUT as the importer makes one: the body and a sha-256 Digest header.</summary>
    protected async Task<HttpStatusCode> PutBinary(string path, byte[] bytes, string? transaction = null,
        string? digest = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, At(path))
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new("text/plain");
        request.Headers.Add("Digest", $"sha-256={digest ?? Sha256Hex(bytes)}");
        using var response = await Send(request, transaction);
        return response.StatusCode;
    }

    protected async Task<HttpStatusCode> StatusOf(string path, string? transaction = null)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Get, At(path)), transaction);
        return response.StatusCode;
    }

    protected async Task<List<string>> Titles(string path, string? transaction = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, At(path));
        request.Headers.Add("Accept", "application/n-triples");
        using var response = await Send(request, transaction);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return TitleTriple().Matches(body).Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>The mementos Fedora lists for a resource: one per OCFL version of an Archival Group.</summary>
    /// <remarks>
    /// Reads ldp:contains however it's compacted: "contains" on 6.5.1, "ldp:contains" on 7.0.0.
    /// This counts what Fedora lists; whether FedoraClient can read the list is
    /// FedoraClientContractTests' business.
    /// </remarks>
    protected async Task<int> VersionCount(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(At(path) + "/fcr:versions")).ForJsonLd();
        using var response = await Send(request, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement[0] : json.RootElement;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is "contains" or "ldp:contains" or "http://www.w3.org/ns/ldp#contains")
            {
                return property.Value.ValueKind == JsonValueKind.Array ? property.Value.GetArrayLength() : 1;
            }
        }
        return 0;
    }

    [GeneratedRegex("<http://purl\\.org/dc/elements/1\\.1/title> \"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex TitleTriple();
}
