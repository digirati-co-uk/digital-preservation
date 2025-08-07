using System.Net;
using DigitalPreservation.Core.Web.Headers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pipeline.API.ApiClients;

public class PreservationApiInterface(IHttpClientFactory httpClientFactory, IAccessTokenProvider? tokenProvider) : IPreservationApiInterface
{
    public async Task<(TResponse? responseData, HttpStatusCode StatusCode)> MakeHttpRequestAsync<TRequest, TResponse>(string url, HttpMethod httpMethod, TRequest requestBody = default, bool handleErrors = false)
    {
        var token = tokenProvider?.GetAccessToken().Result;

        using var client = httpClientFactory.CreateClient("PreservationApi");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage
        {
            Method = httpMethod,
            RequestUri = new Uri(client.BaseAddress + url)
        };

        if (requestBody != null)
        {
            var json = JsonSerializer.Serialize(requestBody);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            await request.Content.LoadIntoBufferAsync();
        }

        var response = await client.SendAsync(request);

        if (handleErrors && !response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrEmpty(responseJson) && response.IsSuccessStatusCode)
            return (default, response.StatusCode);

        var responseData = JsonSerializer.Deserialize<TResponse>(responseJson);

        return (responseData, response.StatusCode);
    }
}
