using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

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

            // https://github.com/dotnet/dotnet-docker/issues/6491
            opts.KnownIPNetworks.Clear();
            opts.KnownProxies.Clear();
        });

    /// <summary>
    /// Binds <see cref="FeatureFlagOptions"/> from the "FeatureFlags" section, for a DI consumer
    /// (a controller, a Razor Page) to read via <c>IOptions&lt;FeatureFlagOptions&gt;</c> instead of
    /// an injected raw <see cref="IConfiguration"/> and a magic string (issue #233 item 2).
    /// </summary>
    public static IServiceCollection AddFeatureFlagOptions(this IServiceCollection serviceCollection,
        IConfiguration configuration)
        => serviceCollection.Configure<FeatureFlagOptions>(configuration.GetSection(FeatureFlagOptions.FeatureFlags));

    /// <summary>
    /// Binds <see cref="FeatureFlagOptions"/> directly, for Program.cs code that decides which
    /// implementation to register before the DI container exists to resolve
    /// <c>IOptions&lt;FeatureFlagOptions&gt;</c> through (issue #233 item 2) - the in-process-vs-SQS
    /// queue choices, and the DisableAuth check that decides whether to wire up auth at all.
    /// </summary>
    public static FeatureFlagOptions GetFeatureFlags(this IConfiguration configuration)
        => configuration.GetSection(FeatureFlagOptions.FeatureFlags).Get<FeatureFlagOptions>() ?? new FeatureFlagOptions();
}