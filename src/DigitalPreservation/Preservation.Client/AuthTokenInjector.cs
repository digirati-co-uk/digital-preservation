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
/// <param name="tokenScope"></param>
/// <param name="logger"></param>
public class AuthTokenInjector(ITokenAcquisition tokenAcquisition, ITokenScope tokenScope, ILogger<AuthTokenInjector> logger) : DelegatingHandler
{
    private async Task<string?> GetBearerToken()
    {
        try
        {
            var scopes = tokenScope.ScopeUri?.Split(" ") ?? [];
            var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(scopes);
            return accessToken;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to obtain Bearer Token");
            return null;
        }
    }


    private async Task SetBearerToken(HttpRequestMessage httpClient)
    {
        var token = await GetBearerToken();

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