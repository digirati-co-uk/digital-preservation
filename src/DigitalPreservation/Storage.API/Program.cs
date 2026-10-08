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
    
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);

    if (useLocalHostedServiceForImport)
    {
        // In-process mode's import queue is an in-memory channel: any job still Active and waiting
        // (never sent a heartbeat, so never actually started) cannot be in the newly-created, empty
        // channel this process just built - it was lost when the previous process stopped. This is
        // the in-process equivalent of an SQS message being dead-lettered, and unlike that case we
        // *can* tell it apart from "still genuinely queued" here, because there is no queue left to
        // still be in. Never do this in SQS mode: there, a waiting job may really still be queued.
        using var startupScope = app.Services.CreateScope();
        var importJobResultStore = startupScope.ServiceProvider.GetRequiredService<IImportJobResultStore>();
        var orphanedResult = await importJobResultStore.FailOrphanedWaitingJobs(
            "never started: the in-process import queue was lost when the Storage API restarted",
            CancellationToken.None);
        if (orphanedResult.Success && orphanedResult.Value > 0)
        {
            app.Logger.LogWarning("Failed {Count} import job(s) left waiting in the in-process queue by a previous run",
                orphanedResult.Value);
        }
        else if (orphanedResult.Failure)
        {
            app.Logger.LogError("Could not check for orphaned waiting import jobs at startup: {CodeAndMessage}",
                orphanedResult.CodeAndMessage());
        }
    }

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