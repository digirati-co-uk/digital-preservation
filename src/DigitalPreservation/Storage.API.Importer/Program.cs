using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Storage.API.Data;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Data;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Data;
using Storage.API.Fedora;
using Storage.API.Infrastructure;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Application starting..");

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog(config =>
    {
        config
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithCorrelationId();
    });
    
    builder.Services
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
            cfg.RegisterServicesFromAssemblyContaining<IFedoraClient>();
        });

    builder.Services
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
        .AddSingleton<IImportJobQueue, SqsImportJobQueue>();
    
    using var host = builder.Build();
    await host.RunAsync();
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
