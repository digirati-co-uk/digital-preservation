using System.Net.Http.Headers;
using DigitalPreservation.CommonApiClient;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace Preservation.Client;

/// <summary>
/// This get an Auth token from MSIL Azure Auth and injects into calls to the
/// Downstream APIs. 
/// </summary>
/// <param name="tokenAcquisition"></param>
/// <param name="tokenScope1"></param>
/// <param name="logger"></param>
public class AuthTokenInjector(ITokenAcquisition tokenAcquisition, ITokenScope tokenScope, ILogger<AuthTokenInjector> logger) : DelegatingHandler
{
    private async Task<string?> GetBearToken()
    {
        try
        {
            var scopes = tokenScope?.ScopeUri?.Split(" ") ?? [];
            var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(scopes);
            return accessToken;
        }
        catch (Exception e)
        {
            logger.LogError(e, $"AuthTokenInjector: Failed to to Bearer Token: {e.Message}");
            return null;
        }
    }


    private async Task SetBearerToken(HttpRequestMessage httpClient)
    {
        var token = await GetBearToken();

        if (token != null)
        {
            httpClient.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await SetBearerToken(request);
        return await base.SendAsync(request, cancellationToken);
    }
}