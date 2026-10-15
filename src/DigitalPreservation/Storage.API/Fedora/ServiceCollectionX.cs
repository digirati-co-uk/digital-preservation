using System.Net.Http.Headers;
using DigitalPreservation.Core.Guard;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Handlers;
using Microsoft.Extensions.Options;
using Storage.API.Fedora.Model;

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
        serviceCollection.Configure<ConverterOptions>(configuration.GetSection(ConverterOptions.Converter));
        serviceCollection
            .AddSingleton<Converters>()
            .AddTransient<TimingHandler>()
            .AddHttpClient<IFedoraClient, FedoraClient>((provider, client) =>
            {
                var fedoraOptions = provider.GetRequiredService<IOptions<FedoraOptions>>().Value;
                client.BaseAddress = fedoraOptions.Root.ThrowIfNull(nameof(fedoraOptions.Root));
                client.DefaultRequestHeaders.WithRequestedBy(componentName);
                client.DefaultRequestHeaders.WithUserAgent($"{componentName}/1.0");
                client.Timeout = TimeSpan.FromMinutes(fedoraOptions.TimeoutMinutes);
                
                // NOTE - this may change depending on how Auth is handled, may be better suited to something in
                // http pipeline (e.g. DelegatingHandler) 
                var credentials = $"{fedoraOptions.AdminUser}:{fedoraOptions.AdminPassword}";
                var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(credentials));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            })
            // 120/30/5 (issue #383 review point 7): honouring these values (rather than the
            // previously hard-coded 300/15/5) means a dead connection now actually gets noticed in
            // about 120 + 5*30 = 270s - comfortably under the NAT gateway's 350s mapping timeout,
            // and in a few minutes rather than the ~62 minutes a naive 120/60/60 would take.
            .ConfigureTcpKeepAlive(true, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(30), 5)
            .AddHttpMessageHandler<TimingHandler>();

        return serviceCollection;
    }


    public static IServiceCollection AddFedoraDB(this IServiceCollection serviceCollection,
        IConfiguration configuration, string connectionStringKey)
    {
        serviceCollection.AddSingleton<FedoraDB>(provider => new FedoraDB(
            provider.GetRequiredService<Converters>(),
            configuration.GetConnectionString(connectionStringKey),
            provider.GetRequiredService<ILogger<FedoraDB>>()));
        return serviceCollection;
    }
}