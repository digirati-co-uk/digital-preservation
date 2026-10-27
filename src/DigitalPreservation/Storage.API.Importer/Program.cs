using Serilog;
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
        cfg.RegisterServicesFromAssemblyContaining<IStorage>();
        cfg.RegisterServicesFromAssemblyContaining<IFedoraClient>();
    });

    // Auth: this host never has a user context, only ever a ClientID/Secret for calling out - there
    // is nothing here yet to gate on the flag, unlike the Storage API's own copy of this read.

    builder.Services.AddStorageCore(builder.Configuration, "Storage-API-Importer");

    builder.Services
        .AddHostedService<ImportJobExecutorService>()
        .AddScoped<ImportJobRunner>()
        .AddSingleton<IImportJobQueue, SqsImportJobQueue>()
        .AddSingleton<IExportQueue, InProcessExportQueue>(); // don't need this but Mediatr does
    
    var app = builder.Build();
    // Deliberately no app.UseForwardedHeaders() here, unlike the Storage API (review feedback on
    // #388): this host serves no HTTP beyond the bare /health check, with no load balancer in front
    // of it (see docs/internals/deployment.md - "the same sections as the Storage API, minus
    // everything to do with serving HTTP"). AddStorageCore still configures the options, since both
    // hosts share it, but applying the middleware here would have nothing to rewrite and nothing to
    // fix - a comment claiming it corrected /health's logged client IP would be describing a load
    // balancer this host doesn't have.
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