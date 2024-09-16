using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Storage.Repository.Common;

public static class ServiceCollectionX
{
    public static IServiceCollection AddFedoraClient(this IServiceCollection serviceCollection,
        IConfiguration configuration, string componentName)
    {
        serviceCollection.Configure<AwsStorageOptions>(configuration.GetSection(AwsStorageOptions.AwsStorage));
    }
}