using System.Net.Http.Headers;
using DigitalPreservation.Core.Guard;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Handlers;
using DigitalPreservation.Core.Web.Headers;
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
    public static IServiceCollection AddDownstreamPreservationClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName)
    {
        serviceCollection.Configure<PreservationOptions>(configuration.GetSection(PreservationOptions.Preservation));
        serviceCollection
            .AddTransient<TimingHandler>()
            .AddTransient<AuthTokenInjector>()
            .AddHttpClient<IPreservationApiClient, PreservationApiClient>((provider, client) =>
            {
                var preservationOptions = provider.GetRequiredService<IOptions<PreservationOptions>>().Value;
                client.BaseAddress = preservationOptions.Root.ThrowIfNull(nameof(preservationOptions.Root));
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.Timeout = TimeSpan.FromMinutes(preservationOptions.TimeoutMinutes);
            })
            // 120/30/5 (issue #383 review point 7): see the comment in Storage.API's
            // ServiceCollectionX.cs on why these values, not the previous 120/60/60.
            .ConfigureTcpKeepAlive(true, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(30), 5)
            .AddHttpMessageHandler<TimingHandler>()
            .AddHttpMessageHandler<AuthTokenInjector>();

        return serviceCollection;
    }

    /// <summary>
    /// Add and configure <see cref="IPreservationApiClient"/> to service collection 
    /// </summary>
    /// <param name="serviceCollection">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="configuration">Current <see cref="IConfiguration"/> object</param>
    /// <param name="componentName">Calling component name, used for "x-requested-by" header</param>
    /// <param name="clientBaseAddress"></param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddMachinePreservationClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName, string? clientBaseAddress = null)
    {
        serviceCollection.Configure<PreservationOptions>(configuration.GetSection(PreservationOptions.Preservation));
        serviceCollection
            .AddTransient<TimingHandler>()
            .AddTransient<MachineAuthTokenInjector>()
            .AddHttpClient<IPreservationApiClient, PreservationApiClient>((provider, client) =>
            {
                var preservationOptions = provider.GetRequiredService<IOptions<PreservationOptions>>().Value;
                client.BaseAddress = !string.IsNullOrEmpty(clientBaseAddress) ? new Uri(clientBaseAddress) : preservationOptions.Root.ThrowIfNull(nameof(preservationOptions.Root));
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.DefaultRequestHeaders.Add("X-Client-Identity", componentName);
                client.Timeout = TimeSpan.FromMinutes(preservationOptions.TimeoutMinutes);
            })
            // 120/30/5 (issue #383 review point 7): see the comment in Storage.API's
            // ServiceCollectionX.cs on why these values, not the previous 120/60/60.
            .ConfigureTcpKeepAlive(true, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(30), 5)
            .AddHttpMessageHandler<TimingHandler>()
            .AddHttpMessageHandler<MachineAuthTokenInjector>();

        return serviceCollection;
    }
}