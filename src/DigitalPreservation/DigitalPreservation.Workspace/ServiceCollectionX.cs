using DigitalPreservation.Mets;
using DigitalPreservation.Mets.AccessConditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Storage.Repository.Common.Mets.StorageImpl;

namespace DigitalPreservation.Workspace;

public static class ServiceCollectionX
{
    /// <summary>
    /// Every registration a consumer needs to build and use a <see cref="WorkspaceManager"/> for a
    /// deposit: the METS loader, parser and manager (plus the manager's options), the metadata and
    /// PREMIS managers, METS storage, and <see cref="WorkspaceManagerFactory"/> itself.
    /// Preservation.API, DigitalPreservation.UI, Pipeline.API and the Deposit Archiver each wired up
    /// the same ten lines by hand (issue #233 item 1, widened by issue #388's sweep from the
    /// original AddMetsManager scope to cover the whole block, once the Archiver turned out to be a
    /// fourth copy).
    /// </summary>
    public static IServiceCollection AddDepositWorkspace(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddSingleton<IMetsLoader, S3MetsLoader>()
            .AddSingleton<IMetsParser, MetsParser>()
            // Issue #188 step 3: migrate a document's IDs whenever it is written, so that editing a
            // pre-#214 METS fixes it rather than leaving it half one generation and half the other.
            .Configure<MetsManagerOptions>(configuration.GetSection("FeatureFlags"))
            .AddSingleton<IMetsManager, MetsManager>()
            .AddSingleton<MetadataManager>()
            .AddSingleton<PremisManager>()
            .AddSingleton<PremisManagerExif>()
            .AddSingleton<PremisEventManagerVirus>()
            .AddSingleton<IMetsStorage, S3MetsStorage>()
            .AddSingleton<WorkspaceManagerFactory>();

        // Derived access conditions (issue #433): the METS writer recomputes them on every write
        // from the installation's profile. Services that have a profile (Preservation API from its
        // configuration, the UI from the API) register their provider BEFORE calling this; anything
        // else gets the null provider and derives nothing.
        services.TryAddSingleton<IAccessConditionProfileProvider, NullAccessConditionProfileProvider>();
        services.TryAddSingleton<IDerivedAccessConditionResolver, ProfileDerivedAccessConditionResolver>();

        return services;
    }
}
