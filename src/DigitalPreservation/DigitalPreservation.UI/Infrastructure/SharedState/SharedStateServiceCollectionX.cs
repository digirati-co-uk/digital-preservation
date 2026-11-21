using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.SecretsManager;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.UI.Infrastructure.SharedState;

public static class SharedStateServiceCollectionX
{
    /// <summary>
    /// Registers data protection and the <see cref="IDistributedCache"/> behind the MSAL token cache and
    /// session, in the store <c>SharedState:Store</c> names (issue #320). See <see cref="SharedStateOptions"/>.
    /// Never fails: a store that is missing configuration is skipped, and the UI keeps that state in
    /// memory as before.
    /// </summary>
    /// <returns>A one-line description of what was configured, for the startup log.</returns>
    public static string AddSharedState(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SharedStateOptions.SharedState);
        services.Configure<SharedStateOptions>(section);
        var settings = section.Get<SharedStateOptions>() ?? new SharedStateOptions();

        services.AddDataProtection().SetApplicationName(settings.ApplicationName);

        if (string.Equals(settings.Store, SharedStateOptions.AwsStore, StringComparison.OrdinalIgnoreCase))
        {
            return AddAwsSharedState(services, configuration, settings);
        }

        services.AddDistributedMemoryCache();
        return string.Equals(settings.Store, SharedStateOptions.MemoryStore, StringComparison.OrdinalIgnoreCase)
            ? "keeping sign-in state in memory (SharedState:Store is Memory)"
            : $"WARNING: unknown SharedState:Store '{settings.Store}'; keeping sign-in state in memory";
    }

    private static string AddAwsSharedState(IServiceCollection services, IConfiguration configuration,
        SharedStateOptions settings)
    {
        // The same account, region and credentials as the UI's S3 access, with short timeouts: a store
        // that is slow to answer is treated as unavailable.
        var aws = configuration.GetAWSOptions("Storage-AWS");
        aws.DefaultClientConfig.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, settings.TimeoutMilliseconds));
        aws.DefaultClientConfig.MaxErrorRetry = 1;

        var parts = new List<string>();

        var prefix = settings.Aws.KeySecretPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add("WARNING: no SharedState:Aws:KeySecretPrefix, so data-protection keys stay in memory " +
                      "and the shared cache can't be read by another task");
        }
        else
        {
            services.AddAWSService<IAmazonSecretsManager>(aws);
            services.AddSingleton(sp => new SecretsManagerXmlRepository(
                // Resolved on first use, inside the repository's error handling.
                () => sp.GetRequiredService<IAmazonSecretsManager>(),
                sp.GetRequiredService<IOptions<SharedStateOptions>>(),
                sp.GetRequiredService<ILogger<SecretsManagerXmlRepository>>()));
            services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
                new ConfigureOptions<KeyManagementOptions>(o =>
                    o.XmlRepository = sp.GetRequiredService<SecretsManagerXmlRepository>()));
            parts.Add($"data-protection keys in Secrets Manager under '{prefix}'");
        }

        var table = settings.Aws.CacheTableName;
        if (string.IsNullOrWhiteSpace(table))
        {
            services.AddDistributedMemoryCache();
            parts.Add("WARNING: no SharedState:Aws:CacheTableName, so the token cache and session stay in memory");
        }
        else
        {
            // AWS's provider builds its own client (TryAddAWSService) unless one is registered; register
            // ours so it gets the short timeouts. Then take its IDistributedCache registration and put
            // the fallback wrapper in front of it.
            services.AddAWSService<IAmazonDynamoDB>(aws);
            services.AddAWSDynamoDBDistributedCache(o =>
            {
                o.TableName = table;
                o.PartitionKeyName = settings.Aws.PartitionKeyName;
                o.TTLAttributeName = settings.Aws.TtlAttributeName;
                o.UseConsistentReads = true;
                o.CreateTableIfNotExists = false;
            });
            var dynamo = services.Last(d => d.ServiceType == typeof(IDistributedCache));
            services.Remove(dynamo);
            services.AddSingleton<IDistributedCache>(sp => new FallbackDistributedCache(
                () => (IDistributedCache)Resolve(dynamo, sp),
                sp.GetRequiredService<IDataProtectionProvider>(),
                sp.GetRequiredService<IOptions<SharedStateOptions>>(),
                sp.GetRequiredService<ILogger<FallbackDistributedCache>>()));
            parts.Add($"token cache and session in DynamoDB table '{table}', falling back to memory");
        }

        return string.Join("; ", parts);
    }

    private static object Resolve(ServiceDescriptor descriptor, IServiceProvider sp) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(sp)
        ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType
                                                 ?? throw new InvalidOperationException("Unusable cache registration"));
}
