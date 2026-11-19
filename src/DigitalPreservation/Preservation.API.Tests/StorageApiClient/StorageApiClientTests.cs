using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Export;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using StorageApiClientType = Storage.Client.StorageApiClient;

namespace Preservation.API.Tests.StorageApiClient;

/// <summary>
/// Issue #388 item 2.2: StorageApiClient's HTTP-call boilerplate was deduplicated onto shared
/// SendForJsonNotNull/SendForJson/SendForResult helpers in CommonApiBase. No test project previously
/// existed for this client at all; these pin the two divergences that were explicitly fixed as part
/// of that move (the missing status check surfaced through GetStorageMap's sibling shape here via
/// GetExport, and FedoraSearch's unescaped query text) alongside a basic success case.
/// </summary>
public class StorageApiClientTests
{
    private static HttpClient FakeClientReturning(HttpResponseMessage response) =>
        FakeClient(_ => response);

    private static HttpClient FakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .ReturnsLazily(call => Task.FromResult(respond(call.GetArgument<HttpRequestMessage>(0)!)));
        return new HttpClient(handler) { BaseAddress = new Uri("http://storage-api") };
    }

    [Fact]
    public async Task GetExport_returns_the_parsed_export_on_200()
    {
        var uri = new Uri("http://storage-api/export/1");
        var httpClient = FakeClientReturning(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new Export
            {
                ArchivalGroup = new Uri("http://storage-api/repository/cc"),
                Destination = new Uri("http://storage-api/export-destination")
            })
        });
        var client = new StorageApiClientType(httpClient, NullLogger<StorageApiClientType>.Instance);

        var result = await client.GetExport(uri);

        Assert.True(result.Success);
        Assert.Equal(new Uri("http://storage-api/repository/cc"), result.Value!.ArchivalGroup);
    }

    /// <summary>GetExport uses SendForJsonNotNull, so an empty body on a 200 is its own failure.</summary>
    [Fact]
    public async Task GetExport_treats_an_empty_200_body_as_a_failure()
    {
        var uri = new Uri("http://storage-api/export/1");
        var httpClient = FakeClientReturning(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        });
        var client = new StorageApiClientType(httpClient, NullLogger<StorageApiClientType>.Instance);

        var result = await client.GetExport(uri);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UnknownError, result.ErrorCode);
    }

    /// <summary>
    /// FedoraSearch used to interpolate the search text into the query string unescaped, unlike
    /// PreservationApiClient.Search's Uri.EscapeDataString(text) - an "&amp;" in the text would add
    /// an extra, attacker-controlled query parameter rather than staying part of the search term.
    /// </summary>
    [Fact]
    public async Task FedoraSearch_escapes_the_search_text_in_the_query_string()
    {
        HttpRequestMessage? captured = null;
        var httpClient = FakeClient(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new DigitalPreservation.Common.Model.Search.SearchCollectiveFedora())
            };
        });
        var client = new StorageApiClientType(httpClient, NullLogger<StorageApiClientType>.Instance);

        await client.FedoraSearch("a&b=evil");

        Assert.NotNull(captured);
        var query = captured!.RequestUri!.Query;
        Assert.Equal("?text=a%26b%3Devil&page=0&pageSize=50", query);
    }
}
