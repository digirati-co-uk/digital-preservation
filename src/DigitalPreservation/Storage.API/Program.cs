using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using LeedsDlipServices.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
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


    if (useAuthFeatureFlag)
    {
        // Auth
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
    }


    builder.Services
        .AddMemoryCache()
        .AddOcfl(builder.Configuration)
        .AddFedoraClient(builder.Configuration, "Storage-API")
        .AddFedoraDB(builder.Configuration, "Fedora")
        .AddStorageAwsAccess(builder.Configuration)
        .AddSingleton<IIdentityService, IIdentityService>()
        .AddScoped<IImportJobResultStore, ImportJobResultStore>() // only for Storage API; happens after above for shared S3
        .AddScoped<IExportResultStore, ExportResultStore>() // only for Storage API; happens after above for shared S3
        .AddStorageHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddStorageContext(builder.Configuration)
        .AddControllers(config =>
        {
            if (useAuthFeatureFlag)
            {
                config.Filters.Add(new AuthorizeFilter());
                config.Filters.Add(new AuthFilterIdentifier());
            }
        });


    // Storage API's in-process queues. These will become separate services later.
    builder.Services
        .AddHostedService<ImportJobExecutorService>()
        .AddScoped<ImportJobRunner>()
        .AddSingleton<IImportJobQueue, InProcessImportJobQueue>();

    builder.Services
        .AddHostedService<ExportExecutorService>()
        .AddScoped<ExportRunner>()
        .AddSingleton<IExportQueue, InProcessExportQueue>();
    
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);

    //Auth
    if (useAuthFeatureFlag)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // TODO - remove this, only used for initial setup
    app.MapGet("/", () => "Storage: Hello World!");
    app.MapControllers();
    app.UseHealthChecks("/health");
    app.Run();
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
    Log.CloseAndFlush();
}

// required for WebApplicationFactory
public partial class Program { }