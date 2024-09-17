using Amazon.S3;
using Storage.Repository.Common;

namespace Storage.API.S3;

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