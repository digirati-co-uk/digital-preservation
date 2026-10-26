using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using Storage.API.Data;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Data;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Data;
using Storage.API.Fedora;
using Storage.API.Ocfl;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

namespace Storage.API.Infrastructure;

public static class ServiceCollectionX
{
    /// <summary>
    /// Add required health checks
    /// </summary>
    public static IServiceCollection AddStorageHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }

    /// <summary>
    /// Everything the Storage API and its separately-deployable Importer both need, registered the
    /// same way in the same order so a new consumer can't wire it up half-right (issue #388): OCFL,
    /// the Fedora client and DB, S3, import/export, the minter, the result stores, health checks,
    /// correlation, the DbContext, and the forwarded-headers options. <paramref name="componentName"/>
    /// is the <c>x-requested-by</c> value the Fedora client sends - "Storage-API" or
    /// "Storage-API-Importer" - so Fedora's own logs can tell which host made a given request.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes MediatR: each host scans a different set of assemblies, reflecting what
    /// it actually handles, so that stays a call each <c>Program.cs</c> makes for itself. It also
    /// excludes anything conditional on a feature flag (auth, which queue implementation, which
    /// hosted services run) - those differ in substance between the two hosts, not just in copying.
    /// </remarks>
    public static IServiceCollection AddStorageCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string componentName)
    {
        services
            .ConfigureForwardedHeaders()
            .AddHttpContextAccessor()
            .AddMemoryCache()
            .AddOcfl(configuration)
            .AddFedoraClient(configuration, componentName)
            .AddFedoraDB(configuration, "Fedora")
            .AddStorageAwsAccess(configuration)
            .AddImportExport(configuration)
            .AddSingleton<IIdentityMinter, IdentityMinter>()
            // Both result stores are registered here - shared by both hosts, not "only for Storage
            // API" as the pre-#388 comment on the Importer's own copy incorrectly said - after the
            // S3 registration above, which they depend on.
            .AddScoped<IImportJobResultStore, ImportJobResultStore>()
            .AddScoped<IExportResultStore, ExportResultStore>()
            .AddStorageHealthChecks()
            .AddCorrelationIdHeaderPropagation()
            .AddStorageContext(configuration);

        services.AddOptions<ImportJobOptions>()
            .Bind(configuration.GetSection(ImportJobOptions.SectionName))
            .Validate(o => o.HeartbeatIntervalIsSafelyBelowWindow(),
                "ImportJobs:HeartbeatIntervalSeconds must be smaller than ImportJobs:HeartbeatWindowMinutes " +
                "x 60 seconds, or a live job's own heartbeat gaps could be mistaken for abandonment.")
            .ValidateOnStart();

        return services;
    }
}