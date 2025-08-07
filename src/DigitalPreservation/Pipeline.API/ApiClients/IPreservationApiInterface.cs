using System.Net;

namespace Pipeline.API.ApiClients;

public interface IPreservationApiInterface
{
    Task<(TResponse? responseData, HttpStatusCode StatusCode)> MakeHttpRequestAsync<TRequest, TResponse>(string url, HttpMethod httpMethod, TRequest requestBody = default, bool handleErrors = false);
}
