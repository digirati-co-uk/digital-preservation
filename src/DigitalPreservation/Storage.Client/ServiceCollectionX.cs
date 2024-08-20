using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Storage.Client;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add and configure <see cref="IStorageApiClient"/> to service collection 
    /// </summary>
    /// <param name="serviceCollection">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="componentName">Calling component name, used for "x-requested-by" header</param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddStorageClient(this IServiceCollection serviceCollection,
        string componentName)
    {
        serviceCollection
            .AddTransient<TimingHandler>()
            .AddHttpClient<IStorageApiClient, StorageApiClient>((provider, client) =>
            {
                var storageOptions = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
                client.BaseAddress = storageOptions.Root;
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.Timeout = TimeSpan.FromMilliseconds(storageOptions.TimeoutMs);
            }).AddHttpMessageHandler<TimingHandler>();

        return serviceCollection;
    }
}