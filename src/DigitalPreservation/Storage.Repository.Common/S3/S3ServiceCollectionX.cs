using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Storage.Repository.Common.S3;

public static class S3ServiceCollectionX
{
    public static IServiceCollection AddStorageAwsAccess(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection
            .AddDefaultAWSOptions(configuration.GetAWSOptions("Storage-AWS"))
            .AddAWSService<IAmazonS3>()
            .Configure<AwsStorageOptions>(configuration.GetSection(AwsStorageOptions.AwsStorage));
        serviceCollection.AddSingleton<Repository.Common.IStorage, Repository.Common.Storage>();
        return serviceCollection;
    }
}