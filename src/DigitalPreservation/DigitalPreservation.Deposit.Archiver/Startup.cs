using Amazon.Lambda.Annotations;
using DigitalPreservation.Deposit.Archiver.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Formatting.Json;

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "AWS Lambda annotations source generator requires an instance method.")]
    public void ConfigureServices(IServiceCollection services)
    {
        var oauthAzureSecret = Environment.GetEnvironmentVariable("OAUTH_AZURE_SECRET")!;
        var clientBaseAddress = Environment.GetEnvironmentVariable("CLIENT_BASE_ADDRESS");

        // NOT blocking request threads. Fetched once - ScopeUri and the client credentials/
        // ResourceUri used to come from two separate calls (one synchronous GetSecretValue, one
        // via this cache) for the same secret.
        var authProvider = SecretsCache.GetAsync(oauthAzureSecret, "eu-west-1").GetAwaiter().GetResult();

        //// Example of creating the IConfiguration object and
        //// adding it to the dependency injection container.
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false);

        var configuration = builder.Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .WriteTo.Console(new JsonFormatter())
            //.WriteTo.File("log-.txt", rollingInterval: RollingInterval.Day) locally for testing
            .CreateLogger();

        var settings = new ArchiverStartupSettings(
            authProvider.ScopeUri,
            authProvider.ClientId,
            authProvider.ClientSecret,
            authProvider.TenantId,
            authProvider.ResourceUri,
            clientBaseAddress);

        services.AddArchiverServices(configuration, settings);
    }
}
