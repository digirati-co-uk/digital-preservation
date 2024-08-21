using DigitalPreservation.Core.Guard;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Preservation.Client;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add and configure <see cref="IPreservationApiClient"/> to service collection 
    /// </summary>
    /// <param name="serviceCollection">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="configuration">Current <see cref="IConfiguration"/> object</param>
    /// <param name="componentName">Calling component name, used for "x-requested-by" header</param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddPreservationClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName)
    {
        serviceCollection.Configure<PreservationOptions>(configuration.GetSection(PreservationOptions.Preservation));
        serviceCollection
            .AddTransient<TimingHandler>()
            .AddHttpClient<IPreservationApiClient, PreservationApiClient>((provider, client) =>
            {
                var preservationOptions = provider.GetRequiredService<IOptions<PreservationOptions>>().Value;
                client.BaseAddress = preservationOptions.Root.ThrowIfNull(nameof(preservationOptions.Root));
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.Timeout = TimeSpan.FromMilliseconds(preservationOptions.TimeoutMs);
            }).AddHttpMessageHandler<TimingHandler>();

        return serviceCollection;
    }
}