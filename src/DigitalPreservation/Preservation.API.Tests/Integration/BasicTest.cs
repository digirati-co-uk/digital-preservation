using Test.Helpers;

namespace Preservation.API.Tests.Integration;

[Trait("Category", "Integration")]
public class BasicTest : IClassFixture<DigitalPreservationAppFactory<Program>>
{
    private readonly HttpClient httpClient;

    public BasicTest(DigitalPreservationAppFactory<Program> factory)
    {
        httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsNewCorrelationId_IfNoneSpecified()
    {
        var response = await httpClient.GetAsync("/");

        response.Headers.Should().ContainKey("x-correlation-id")
            .WhoseValue.Should().NotBeNull();
    }
    
    [Theory]
    [InlineData("X-Correlation-Id")]
    [InlineData("x-correlation-id")]
    public async Task Get_ReturnsProvidedCorrelationId_CaseInsensitive(string header)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(header, nameof(Get_ReturnsProvidedCorrelationId_CaseInsensitive));
     
        var response = await httpClient.SendAsync(request);

        response.Headers.Should().ContainKey("x-correlation-id")
            .WhoseValue.Should().BeEquivalentTo(nameof(Get_ReturnsProvidedCorrelationId_CaseInsensitive));
    }
}