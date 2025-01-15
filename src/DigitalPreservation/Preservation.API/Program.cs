using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Preservation.API.Data;
using Preservation.API.Infrastructure;
using Preservation.API.Mutation;
using Serilog;
using Storage.Client;
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

    //Auth 
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    if (useAuthFeatureFlag)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches(); 
    }
    
    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
        })
        .AddStorageAwsAccess(builder.Configuration)
        .AddStorageClient(builder.Configuration, "Preservation-API")
        .AddResourceMutator(builder.Configuration)
        .AddSingleton<IIdentityService, TemporaryNonCheckingIdentityService>()
        .AddPreservationHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddPreservationContext(builder.Configuration)
        .AddControllers(config =>
        {
            //Auth All Controllers
            if (useAuthFeatureFlag)
            {
               config.Filters.Add(new AuthorizeFilter());
            }
        });

   
    
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);
    
    if (useAuthFeatureFlag)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

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