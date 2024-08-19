using DigitalPreservation.Core.Guard;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Storage.Client;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add and configure <see cref="IStorageApiClient"/> to service collection 
    /// </summary>
    /// <param name="serviceCollection">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="configuration">Current <see cref="IConfiguration"/> object</param>
    /// <param name="componentName">Calling component name, used for "x-requested-by" header</param>
    /// <param name="storageSectionName">Config section name containing storage options</param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddStorageClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName, string storageSectionName = "Storage")
    {
        var storageSection = configuration.GetSection(storageSectionName);
        var storageOptions = storageSection.Get<StorageOptions>().ThrowIfNull(nameof(storageSection));
        var storageRoot = storageOptions.Root;

        serviceCollection
            .AddTransient<TimingHandler>()
            .AddHttpClient<IStorageApiClient, StorageApiClient>(client =>
            {
                client.BaseAddress = storageRoot;
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.Timeout = TimeSpan.FromMilliseconds(storageOptions.TimeoutMs);
            }).AddHttpMessageHandler<TimingHandler>();

        return serviceCollection;
    }
}