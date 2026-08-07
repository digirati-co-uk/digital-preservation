using System.Net.Http.Headers;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.Extensions.Logging;

namespace Preservation.Client;

/// <summary>
/// Gets a client-credentials Bearer token from <see cref="IAccessTokenProvider"/> and injects it
/// into calls to Preservation API for machine-to-machine callers. Runs per-request rather than once
/// at HttpClient construction time, so a client used across a long-running job (an ExecutePipelineJob
/// handler can live for the job's whole duration) always sends a current token instead of one that
/// may have expired partway through - see LPII-135.
/// </summary>
public class MachineAuthTokenInjector(IAccessTokenProvider tokenProvider, ILogger<MachineAuthTokenInjector> logger) : DelegatingHandler
{
    private async Task SetBearerToken(HttpRequestMessage request)
    {
        var token = await tokenProvider.GetAccessToken();

        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            logger.LogWarning("No access token available to attach to request {Uri}", request.RequestUri);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await SetBearerToken(request);
        return await base.SendAsync(request, cancellationToken);
    }
}
