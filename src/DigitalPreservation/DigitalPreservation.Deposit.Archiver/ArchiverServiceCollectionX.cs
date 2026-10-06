using Amazon.S3;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Preservation.Client;
using Storage.Repository.Common.Mets.StorageImpl;
using Storage.Repository.Common.S3;

namespace DigitalPreservation.Deposit.Archiver;

public static class ArchiverServiceCollectionX
{
    /// <summary>
    /// Every service the Deposit Archiver Lambda registers. Takes only configuration and a
    /// settings record - no environment variables, no Secrets Manager call - so it can be built
    /// and validated (ArchiverServiceRegistrationTests) without AWS credentials. Registrations,
    /// order and lifetimes match Startup.ConfigureServices before this split; this is a move, not
    /// a change.
    /// </summary>
    public static IServiceCollection AddArchiverServices(
        this IServiceCollection services, IConfiguration configuration, ArchiverStartupSettings settings)
    {
        services.AddSingleton<IConfiguration>(configuration);

        services.AddSingleton<ITokenScope>(_ => new TokenScope(settings.ScopeUri));

        services.ConfigureForwardedHeaders()
            .AddHttpContextAccessor()
            .AddMemoryCache()
            .AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<WorkspaceManagerFactory>();
            })
            .AddMachinePreservationClient(configuration, "ArchiverLambda", settings.ClientBaseAddress);

        var accessTokenProviderOptions = new AccessTokenProviderOptions
        {
            ClientId = settings.ClientId,
            ClientSecret = settings.ClientSecret,
            TenantId = settings.TenantId,
            ResourceUri = settings.ResourceUri
        };
        services.AddAccessTokenProvider(accessTokenProviderOptions);

        services.AddAWSService<IAmazonS3>();

        services.AddStorageAwsAccess(configuration);
        services.AddSingleton<IIdentityMinter, IdentityMinter>();
        services.AddSingleton<IMetsLoader, S3MetsLoader>();
        services.AddSingleton<IMetsParser, MetsParser>();
        services.Configure<MetsManagerOptions>(configuration.GetSection("FeatureFlags"));
        services.AddSingleton<IMetsManager, MetsManager>();
        services.AddSingleton<MetadataManager>();
        services.AddSingleton<PremisManager>();
        services.AddSingleton<PremisManagerExif>();
        services.AddSingleton<PremisEventManagerVirus>();
        services.AddSingleton<IMetsStorage, S3MetsStorage>();
        services.AddSingleton<WorkspaceManagerFactory>();

        return services;
    }
}
