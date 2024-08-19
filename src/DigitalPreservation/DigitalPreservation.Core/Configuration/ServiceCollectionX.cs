using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Http;

namespace DigitalPreservation.Core.Configuration;

public static class ServiceCollectionX
{
    /// <summary>
    /// Configure <see cref="ForwardedHeadersOptions"/>
    /// </summary>
    /// <param name="serviceCollection">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="forwardedHeaders">Optional <see cref="ForwardedHeaders"/> config, if not provided defaults to
    /// X-Forwarded-Host and X-Forwarded-Proto</param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection ConfigureForwardedHeaders(this IServiceCollection serviceCollection,
        ForwardedHeaders? forwardedHeaders = null)
        => serviceCollection.Configure<ForwardedHeadersOptions>(opts =>
        {
            opts.ForwardedHeaders =
                forwardedHeaders ?? ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
        });
}