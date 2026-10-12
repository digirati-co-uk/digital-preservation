using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Storage.API.Web;
using Serilog;
using Storage.API.Data;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Data;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Data;
using Storage.API.Fedora;
using Storage.API.Infrastructure;
using Storage.API.Ocfl;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Application starting..");

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((hostContext, loggerConfiguration)
        => loggerConfiguration
            .ReadFrom.Configuration(hostContext.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithCorrelationId());

    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
        });

    //Auth enabled flag
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    var useLocalHostedServiceForImport = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForImport");
    var useLocalHostedServiceForExport = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForExport");

    // ImportJobRunner (the heartbeat writer) only runs in this host in in-process mode; in SQS mode
    // it's the separate Importer. Both hosts validate unconditionally regardless of mode - see
    // Storage.API.Importer/Program.cs for its copy of this call - so a deployment can't end up with
    // one host's settings checked and the other's not depending on which mode happens to be live.
    ImportJobHeartbeatSettings.Validate(builder.Configuration);

    if (useAuthFeatureFlag)
    {
        // Auth
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
    }

    // Allow-list of known machine callers, keyed by signed azp/appid (RFC-0001 Phase 0).
    // Registered unconditionally so AuthFilterIdentifier can resolve it even with auth disabled.
    builder.Services.AddClientDirectory(builder.Configuration);

    builder.Services
        .AddMemoryCache()
        .AddOcfl(builder.Configuration)
        .AddFedoraClient(builder.Configuration, "Storage-API")
        .AddFedoraDB(builder.Configuration, "Fedora")
        .AddStorageAwsAccess(builder.Configuration)
        .AddImportExport(builder.Configuration)
        .AddSingleton<IIdentityMinter, IdentityMinter>()
        .AddScoped<IImportJobResultStore, ImportJobResultStore>() // only for Storage API; happens after above for shared S3
        .AddScoped<IExportResultStore, ExportResultStore>() // only for Storage API; happens after above for shared S3
        .AddStorageHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddStorageContext(builder.Configuration)
        .AddControllers(config =>
        {
            config.Filters.Add<RepositoryPathFilter>();
            if (useAuthFeatureFlag)
            {
                config.Filters.Add(new AuthorizeFilter());
                config.Filters.Add(new AuthFilterIdentifier());
            }
        });


    builder.Services.AddApiSwaggerGen("Storage API", useAuthFeatureFlag);


    if (useLocalHostedServiceForImport)
    {
        builder.Services
            .AddHostedService<ImportJobExecutorService>()
            .AddScoped<ImportJobRunner>()
            .AddSingleton<IImportJobQueue, InProcessImportJobQueue>(); // <= SqsExportQueue
    }
    else
    {
        // The Import Service is a separate ECR, a separate scalable service...
        builder.Services.AddSingleton<IImportJobQueue, SqsImportJobQueue>();
    }

    if (useLocalHostedServiceForExport)
    {
        // ...but export is much less used, and can run alongside the Storage API as
        // a Hosted Service
        builder.Services
            .AddHostedService<ExportExecutorService>()
            .AddScoped<ExportRunner>()
            .AddSingleton<IExportQueue, InProcessExportQueue>(); // <= SqsExportQueue
    }
    else
    {
        throw new NotSupportedException("Separate export service not yet implemented!");
    }

    // Unconditional, regardless of which mode actually executes import jobs (in-process or the
    // separate SQS Importer): this is the one host that is always running, and the sweep's reap is
    // idempotent (a conditional UPDATE), so it is safe even if more than one Storage API instance
    // runs it at the same tick.
    builder.Services.AddHostedService<ImportJobReapService>();


    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);

    await app.FailOrphanedWaitingImportJobsIfInProcessAsync(useLocalHostedServiceForImport);

    //Auth
    if (useAuthFeatureFlag)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Storage API");
    });

    // TODO - remove this, only used for initial setup
    app.MapGet("/", () => "Storage: Hello World!");
    app.MapControllers();
    app.UseHealthChecks("/health");
    await app.RunAsync();
}
catch (HostAbortedException)
{
    // No-op - required when adding migrations,
    // See: https://github.com/dotnet/efcore/issues/29809#issuecomment-1345132260
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception on startup");
}
finally
{
    Log.Information("Shut down complete");
    await Log.CloseAndFlushAsync();
}

// required for WebApplicationFactory
public partial class Program
{
    protected Program() { }
}