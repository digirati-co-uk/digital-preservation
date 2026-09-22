using DigitalPreservation.Core.Auth;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Primitives;

namespace DigitalPreservation.Core.Web.Headers;

public static class ApplicationBuilderX
{
    /// <summary>
    /// Propagate x-correlation-id header to any downstream calls.
    /// NOTE: This will be added to ALL httpClient requests.
    /// </summary>
    public static IServiceCollection AddCorrelationIdHeaderPropagation(this IServiceCollection services)
    {
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, HeaderPropagationMessageHandlerBuilderFilter>();
        return services;
    }
}

/// <summary>
///
/// </summary>
/// <param name="contextAccessor"></param>
/// <param name="serviceProvider">IAccessTokenProvider is resolved from this lazily, per handler
/// chain, NOT constructor-injected: AccessTokenProvider depends on IHttpClientFactory, and the
/// factory's own constructor consumes every registered IHttpMessageHandlerBuilderFilter, so
/// constructor injection here closes that triangle into a circular resolution — undetectable to
/// DI's creation-time cycle check (the factory is registered via a lambda) and therefore an
/// unbounded runtime recursion that hangs the process at the first client build. By the time a
/// handler chain is built the factory singleton exists, so resolving here is cycle-free; where no
/// provider is registered (Storage API, the UI) it still resolves to null as before.</param>
internal class HeaderPropagationMessageHandlerBuilderFilter(IHttpContextAccessor contextAccessor, IServiceProvider serviceProvider)
    : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            // Never attach to the client AccessTokenProvider mints tokens through. This handler
            // would call GetAccessToken to decorate the token request itself; mid-mint the cache
            // cannot be populated yet, so that inner call mints again, synchronously, until the
            // stack overflows. Platform auth headers have no business on a request to Entra anyway.
            if (builder.Name != AccessTokenProvider.HttpClientName)
            {
                var tokenProvider = serviceProvider.GetService<IAccessTokenProvider>();
                builder.AdditionalHandlers.Add(new PropagateCorrelationIdHandler(contextAccessor, tokenProvider));
            }
            next(builder);
        };
    }
}

/// <summary>
/// A DelegatingHandler that propagates x-correlation-id to downstream service
/// </summary>

public class PropagateCorrelationIdHandler(IHttpContextAccessor contextAccessor, IAccessTokenProvider? tokenProvider) : DelegatingHandler
{
    private const string CorrelationHeaderKey = "x-correlation-id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (contextAccessor.HttpContext == null)
        {
            SetMachineToken(request);

            return base.SendAsync(request, cancellationToken);
        }

        var headerValue = contextAccessor.HttpContext.TryGetHeaderValue(CorrelationHeaderKey);
        if (!string.IsNullOrEmpty(headerValue))
        {
            AddCorrelationId(request, headerValue);
        }


        //Pass Bearer token to downstream API 
        var bearerToken = contextAccessor.HttpContext.TryGetHeaderValue("Authorization");
        if (!string.IsNullOrEmpty(bearerToken) && request.Headers.Authorization == null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", bearerToken);
        }
        else
        {
            //Add Bearer token to request if not already present
            SetMachineToken(request);
        }

        //Pass Machine Token Downstream
        var machineToken = contextAccessor.HttpContext.TryGetHeaderValue(AuthFilterIdentifier.MachineHeaderName);
        if (!string.IsNullOrEmpty(machineToken))
        {
            request.Headers.TryAddWithoutValidation(AuthFilterIdentifier.MachineHeaderName, machineToken);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private void SetMachineToken(HttpRequestMessage request)
    {
        //Add Bearer token to request if not already present
        var token = tokenProvider?.GetAccessToken().Result;

        if (token is null) return;

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation(AuthFilterIdentifier.MachineHeaderName, "api-call");
    }

    private static void AddCorrelationId(HttpRequestMessage request, string? correlationId) 
        => request.Headers.TryAddWithoutValidation(CorrelationHeaderKey, [correlationId]);
}

internal static class HttpContextX
{
    /// <summary>
    /// Attempt to find specified header in current Request, optionally checking current Response
    /// </summary>
    public static string? TryGetHeaderValue(this HttpContext? httpContext, string headerKey, bool checkResponse = true)
    {
        if (httpContext == null) return null;
        
        if (TryGetHeaderValue(httpContext.Request.Headers, headerKey, out var fromRequest))
        {
            return fromRequest;
        }

        if (checkResponse && TryGetHeaderValue(httpContext.Response.Headers, headerKey, out var fromResponse))
        {
            return fromResponse;
        }
        
        return null;
    }
    
    private static bool TryGetHeaderValue(IHeaderDictionary headers, string headerKey, out string? correlationId)
    {
        correlationId = null;
        if (headers.TryGetValue(headerKey, out var values))
        {
            correlationId = values.FirstOrDefault();
            return !StringValues.IsNullOrEmpty(correlationId);
        }

        return false;
    }
}