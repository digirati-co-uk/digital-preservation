using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Headers;
using Storage.API.Web;
using Serilog;
using Storage.API.Data;
using Storage.API.Features.Export;
using Storage.API.Features.Import;
using Storage.API.Fedora;
using Storage.API.Infrastructure;
using Storage.Repository.Common;

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

    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining<Program>();
        cfg.RegisterServicesFromAssemblyContaining<IStorage>();
    });

    //Auth enabled flag
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    var useLocalHostedServiceForImport = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForImport");
    var useLocalHostedServiceForExport = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForExport");

    if (useAuthFeatureFlag)
    {
        // Auth
        builder.Services.AddPlatformApiAuthentication(builder.Configuration);
    }

    // Allow-list of known machine callers, keyed by signed azp/appid (RFC-0001 Phase 0).
    // Registered unconditionally so AuthFilterIdentifier can resolve it even with auth disabled.
    builder.Services.AddClientDirectory(builder.Configuration);

    builder.Services
        .AddStorageCore(builder.Configuration, "Storage-API")
        .AddControllers(config =>
        {
            config.Filters.Add<RepositoryPathFilter>();
            if (useAuthFeatureFlag)
            {
                config.AddPlatformAuthorizationFilters();
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