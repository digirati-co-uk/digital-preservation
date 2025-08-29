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
            .ConfigureTcpKeepAlive(true, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(60), 60)
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
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddMachinePreservationClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName)
    {
        serviceCollection.Configure<PreservationOptions>(configuration.GetSection(PreservationOptions.Preservation));
        serviceCollection
            .AddTransient<TimingHandler>()
            //.AddTransient<AuthTokenInjector>()
            .AddHttpClient<IPreservationApiClient, PreservationApiClient>((provider, client) =>
            {
                var tokenProvider = provider.GetRequiredService<IAccessTokenProvider>();
                var token = tokenProvider.GetAccessToken().Result;
                var preservationOptions = provider.GetRequiredService<IOptions<PreservationOptions>>().Value;
                client.BaseAddress = preservationOptions.Root.ThrowIfNull(nameof(preservationOptions.Root));
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Add("x-client-identity","PipelineApi");
                client.Timeout = TimeSpan.FromMinutes(preservationOptions.TimeoutMinutes);
            })
            .ConfigureTcpKeepAlive(true, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(60), 60)
            .AddHttpMessageHandler<TimingHandler>();
            //.AddHttpMessageHandler<AuthTokenInjector>();

        return serviceCollection;
    }
}