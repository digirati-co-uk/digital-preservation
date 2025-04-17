using Amazon.SimpleNotificationService;
using Amazon.SQS;

namespace Storage.API.Features.Import;

public static class ServiceCollectionX
{
    public static IServiceCollection AddImportExport(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection
            .Configure<ImportOptions>(configuration.GetSection(ImportOptions.ImportExport))
            .AddAWSService<IAmazonSQS>()
            .AddAWSService<IAmazonSimpleNotificationService>();
        return serviceCollection;
    }
}