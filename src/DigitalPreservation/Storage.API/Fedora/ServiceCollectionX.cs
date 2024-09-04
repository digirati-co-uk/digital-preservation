using System.Net.Http.Headers;
using DigitalPreservation.Core.Guard;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Handlers;
using Microsoft.Extensions.Options;

namespace Storage.API.Fedora;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add and configure <see cref="IFedoraClient"/> to service collection 
    /// </summary>
    /// <param name="serviceCollection">Current <see cref="IServiceCollection"/> object</param>
    /// <param name="configuration">Current <see cref="IConfiguration"/> object</param>
    /// <param name="componentName">Calling component name, used for "x-requested-by" header</param>
    /// <returns>Modified service collection</returns>
    public static IServiceCollection AddFedoraClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName)
    {
        serviceCollection.Configure<FedoraOptions>(configuration.GetSection(FedoraOptions.Fedora));
        serviceCollection
            .AddTransient<TimingHandler>()
            .AddHttpClient<IFedoraClient, FedoraClient>((provider, client) =>
            {
                var fedoraOptions = provider.GetRequiredService<IOptions<FedoraOptions>>().Value;
                client.BaseAddress = fedoraOptions.Root.ThrowIfNull(nameof(fedoraOptions.Root));
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.Timeout = TimeSpan.FromMilliseconds(fedoraOptions.TimeoutMs);
                
                // NOTE - this may change depending on how Auth is handled, may be better suited to something in
                // http pipeline (e.g. DelegatingHandler) 
                var credentials = $"{fedoraOptions.AdminUser}:{fedoraOptions.AdminPassword}";
                var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            }).AddHttpMessageHandler<TimingHandler>();

        return serviceCollection;
    }
}