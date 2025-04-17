using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
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
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
            cfg.RegisterServicesFromAssemblyContaining<IFedoraClient>();
        });
    
    
    //Auth enabled flag - can still use this, eg for local testing scenarios
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    if (useAuthFeatureFlag)
    {
        // Here we will need some auth config that allows Storage.API.Importer to have a ClientID and Secret
        // It _only_ makes calls this way because it never has a user context
    }
    
    builder.Services
        .AddOcfl(builder.Configuration)
        .AddMemoryCache()
        .AddFedoraClient(builder.Configuration, "Storage-API-IIIF-Builder")
        .AddFedoraDB(builder.Configuration, "Fedora")
        .AddStorageAwsAccess(builder.Configuration)
        .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
        .AddImportExport(builder.Configuration)
        .AddSingleton<IIdentityMinter, IdentityMinter>()
        .AddScoped<IImportJobResultStore, ImportJobResultStore>() // only for Storage API; happens after above for shared S3
        .AddScoped<IExportResultStore, ExportResultStore>() // only for Storage API; happens after above for shared S3
        .AddStorageHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddStorageContext(builder.Configuration);
    
    builder.Services
        .AddHostedService<ImportJobExecutorService>()
        .AddScoped<ImportJobRunner>()
        .AddSingleton<IImportJobQueue, SqsImportJobQueue>()
        .AddSingleton<IExportQueue, InProcessExportQueue>(); // don't need this but Mediatr does
    
    var app = builder.Build();
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