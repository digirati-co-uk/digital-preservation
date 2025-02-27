using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Workspace;
using Preservation.API.Data;
using Preservation.API.Infrastructure;
using Preservation.API.Mutation;
using Serilog;
using Storage.Client;
using Storage.Repository.Common;
using Storage.Repository.Common.Mets;
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
            cfg.RegisterServicesFromAssemblyContaining<WorkspaceManagerFactory>();
        })
        .AddStorageAwsAccess(builder.Configuration)
        .AddStorageClient(builder.Configuration, "Preservation-API")
        .AddResourceMutator(builder.Configuration)
        .AddSingleton<IIdentityService, TemporaryNonCheckingIdentityService>()
        .AddSingleton<IMetsParser, MetsParser>()
        .AddSingleton<IMetsManager, MetsManager>()
        .AddSingleton<WorkspaceManagerFactory>()
        .AddPreservationHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddPreservationContext(builder.Configuration)
        .AddControllers();
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);
    
    // TODO - remove this, only used for initial setup
    app.MapGet("/", () => "Preservation: Hello World!");
    app.MapGet("/test", (IConfiguration configuration) => $"Config value 'TestVal': {configuration["TestVal"]} ");
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