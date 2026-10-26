using System.Net;
using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using FakeItEasy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Preservation.API.Tests.PreservationApiClient;

public class PreservationApiClientTests
{
    [Fact]
    public async Task DeactivateDeposit_returns_ok_on_200()
    {
        // Arrange
        var deposit = new Deposit { Id = new Uri("http://api/deposits/123") };

        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call =>
                call.Method.Name == "SendAsync" &&
                call.GetArgument<HttpRequestMessage>(0)!.Method == HttpMethod.Put &&
                call.GetArgument<HttpRequestMessage>(0)!.RequestUri!.ToString().EndsWith("/deposits/123/deactivate"))
            .WithReturnType<Task<HttpResponseMessage>>()
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new Client.PreservationApiClient(httpClient, new MemoryCache(new MemoryCacheOptions()), NullLogger<Client.PreservationApiClient>.Instance);

        // Act
        var result = await client.DeactivateDeposit(deposit, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task DeactivateDeposit_returns_fail_on_non_success()
    {
        var deposit = new Deposit { Id = new Uri("http://api/deposits/123") };

        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new Client.PreservationApiClient(httpClient, new MemoryCache(new MemoryCacheOptions()), NullLogger<Client.PreservationApiClient>.Instance);

        var result = await client.DeactivateDeposit(deposit, CancellationToken.None);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Issue #388 item 2.2: LockDeposit, ReleaseDepositLock, RunPipeline, ActivateDeposit and
    /// DeactivateDeposit previously had no try/catch at all, so a thrown exception (a connection
    /// failure, a timeout) propagated out of the client uncaught rather than becoming a Result.
    /// Migrating them onto the shared SendForResult helper closes that gap as a side effect of
    /// removing the duplication. This pins the fix directly: the client must not throw.
    /// </summary>
    [Fact]
    public async Task ActivateDeposit_catches_a_thrown_exception_instead_of_propagating_it()
    {
        var deposit = new Deposit { Id = new Uri("http://api/deposits/123") };

        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .Throws(new HttpRequestException("connection refused"));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new Client.PreservationApiClient(httpClient, new MemoryCache(new MemoryCacheOptions()), NullLogger<Client.PreservationApiClient>.Instance);

        var result = await client.ActivateDeposit(deposit, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UnknownError, result.ErrorCode);
    }

    [Fact]
    public async Task GetDeposit_returns_the_parsed_deposit_on_200()
    {
        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Deposit { Id = new Uri("http://api/deposits/123") })
            }));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new Client.PreservationApiClient(httpClient, new MemoryCache(new MemoryCacheOptions()), NullLogger<Client.PreservationApiClient>.Instance);

        var result = await client.GetDeposit("123", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new Uri("http://api/deposits/123"), result.Value!.Id);
    }

    /// <summary>
    /// GetDeposit returns Result&lt;Deposit?&gt; (the null-tolerant SendForJson), so an empty body on
    /// a 200 is its own failure (NotFound, "No resource at ...") rather than a null success value.
    /// </summary>
    [Fact]
    public async Task GetDeposit_treats_an_empty_200_body_as_a_failure()
    {
        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
            }));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new Client.PreservationApiClient(httpClient, new MemoryCache(new MemoryCacheOptions()), NullLogger<Client.PreservationApiClient>.Instance);

        var result = await client.GetDeposit("123", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// GetAllAgents returns Result&lt;List&lt;Uri&gt;&gt; (the never-null SendForJsonNotNull), the
    /// other half of the two generic helpers.
    /// </summary>
    [Fact]
    public async Task GetAllAgents_returns_the_parsed_list_on_200()
    {
        var handler = A.Fake<HttpMessageHandler>();
        A.CallTo(handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<Uri> { new("http://api/agents/1") })
            }));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api") };
        var client = new Client.PreservationApiClient(httpClient, new MemoryCache(new MemoryCacheOptions()), NullLogger<Client.PreservationApiClient>.Instance);

        var result = await client.GetAllAgents(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new Uri("http://api/agents/1"), Assert.Single(result.Value!));
    }
}
