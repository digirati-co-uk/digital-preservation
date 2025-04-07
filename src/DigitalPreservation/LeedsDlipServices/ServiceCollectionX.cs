using DigitalPreservation.Core.Guard;
using LeedsDlipServices.Identity;
using LeedsDlipServices.MVPCatalogueApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeedsDlipServices;


public static class ServiceCollectionX
{   
    public static IServiceCollection AddIdentityServiceClient(
        this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        serviceCollection.Configure<IdentityOptions>(configuration.GetSection(IdentityOptions.IdentityOptionsName));
        serviceCollection.AddHttpClient<IIdentityService, IdentityService>((provider, client) =>
            {
                var identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
                client.BaseAddress = identityOptions.Root.ThrowIfNull(nameof(identityOptions.Root));
                client.DefaultRequestHeaders.Add(identityOptions.ApiKeyHeader, identityOptions.ApiKeyValue);
                client.Timeout = TimeSpan.FromMilliseconds(identityOptions.TimeoutMs);
            });

        return serviceCollection;
    }
    
    public static IServiceCollection AddMvpCatalogueClient(
        this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        serviceCollection.Configure<CatalogueOptions>(configuration.GetSection(CatalogueOptions.CatalogueOptionsName));
        serviceCollection.AddHttpClient<IMvpCatalogue, MvpCatalogue>((provider, client) =>
        {
            var catalogueOptions = provider.GetRequiredService<IOptions<CatalogueOptions>>().Value;
            client.BaseAddress = catalogueOptions.Root.ThrowIfNull(nameof(catalogueOptions.Root));
            client.DefaultRequestHeaders.Add(catalogueOptions.ApiKeyHeader, catalogueOptions.ApiKeyValue);
            client.Timeout = TimeSpan.FromMilliseconds(catalogueOptions.TimeoutMs);
        });

        return serviceCollection;
    }
}