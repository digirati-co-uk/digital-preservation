using Amazon.SimpleNotificationService;
using Amazon.SQS;
using DigitalPreservation.Common.Model.PipelineApi;

namespace Pipeline.API.Features.Pipeline;

public static class ServiceCollectionPipeline
{
    public static IServiceCollection AddPipeline(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection
            .Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.PipelineJob))
            .AddAWSService<IAmazonSQS>()
            .AddAWSService<IAmazonSimpleNotificationService>();
        return serviceCollection;
    }
}