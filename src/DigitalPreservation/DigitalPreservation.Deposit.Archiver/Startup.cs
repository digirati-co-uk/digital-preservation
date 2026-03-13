using Amazon;
using Amazon.Lambda.Annotations;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Preservation.Client;
using Serilog;
using Serilog.Formatting.Json;
using Storage.Repository.Common;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;
using Storage.Repository.Common.S3;

namespace DigitalPreservation.Deposit.Archiver;

[LambdaStartup]
public class Startup
{
    /// <summary>
    /// Services for Lambda functions can be registered in the services dependency injection container in this method. 
    ///
    /// The services can be injected into the Lambda function through the containing type's constructor or as a
    /// parameter in the Lambda function using the FromService attribute. Services injected for the constructor have
    /// the lifetime of the Lambda compute container. Services injected as parameters are created within the scope
    /// of the function invocation.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        var fromServerlessTemplateoauthAzureSecret = Environment.GetEnvironmentVariable("oauthAzureSecret");
        var secretJsonString = GetSecretValue(fromServerlessTemplateoauthAzureSecret!, "eu-west-1");

        var secretModel = System.Text.Json.JsonSerializer.Deserialize<AuthProviderModel>(secretJsonString);

        var netEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        //// Example of creating the IConfiguration object and
        //// adding it to the dependency injection container.
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            //.AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{netEnv}.json", false, true)
            .AddJsonFile("appsettings.json", false);

        var configuration = builder.Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .WriteTo.Console(new JsonFormatter())
            //.WriteTo.File("log-.txt", rollingInterval: RollingInterval.Day) locally for testing
            .CreateLogger();

        Log.Logger.Information("Secret model {secretModel}", secretModel);

        services.AddSingleton<IConfiguration>(configuration);

        services.AddSingleton<ITokenScope>(x => new TokenScope(secretModel?.ScopeUri));

        services.ConfigureForwardedHeaders()
            .AddHttpContextAccessor()
            .AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<WorkspaceManagerFactory>();
            })
            .AddMachinePreservationClient(configuration, "ArchiverLambda");

        var accessTokenProviderOptions = new AccessTokenProviderOptions
        {
            ClientId = secretModel?.ClientId,
            ClientSecret = secretModel?.ClientSecret,
            TenantId = secretModel?.TenantId
        };
        services.AddSingleton<IAccessTokenProviderOptions>(accessTokenProviderOptions);
        services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();

        services.AddAWSService<IAmazonS3>(); //AMAzon.s3

        services.AddStorageAwsAccess(configuration);
        services.AddSingleton<IIdentityMinter, IdentityMinter>();
        services.AddSingleton<IMetsLoader, S3MetsLoader>();
        services.AddSingleton<IMetsParser, MetsParser>();
        services.AddSingleton<IMetsManager, MetsManager>();
        services.AddSingleton<IMetadataManager, MetadataManager>();
        services.AddSingleton<IPremisManager<FileFormatMetadata>, PremisManager>();
        services.AddSingleton<IPremisManager<ExifMetadata>, PremisManagerExif>();
        services.AddSingleton<IPremisEventManager<VirusScanMetadata>, PremisEventManagerVirus>();
        services.AddSingleton<IMetsStorage, S3MetsStorage>();
        services.AddSingleton<WorkspaceManagerFactory>();
    }

    private string GetSecretValue(string secretName, string region)
    {
        var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));

        var request = new GetSecretValueRequest()
        {
            SecretId = secretName,
            VersionStage = "AWSCURRENT"
        };

        var response = client.GetSecretValueAsync(request).GetAwaiter().GetResult();
        return response.SecretString;
    }
}
