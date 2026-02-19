using System.Net;
using DigitalPreservation.Common.Model.PreservationApi;
using FakeItEasy;
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
        var client = new Client.PreservationApiClient(httpClient, NullLogger<Client.PreservationApiClient>.Instance);

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
        var client = new Client.PreservationApiClient(httpClient, NullLogger<Client.PreservationApiClient>.Instance);

        var result = await client.DeactivateDeposit(deposit, CancellationToken.None);

        Assert.False(result.Success);
    }
}
