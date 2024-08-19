using DigitalPreservation.Core;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using MediatR;
using Preservation.API.Features.Storage.Requests;
using Preservation.API.Infrastructure;
using Serilog;
using Storage.Client;

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
            .Enrich.WithCorrelationId(addValueIfHeaderAbsence: true));
    
    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>())
        .AddStorageClient(builder.Configuration, "Preservation-API")
        .AddPreservationHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddControllers();
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders();
    
    // TODO - remove these, they are only used for initial setup
    app.MapGet("/", () => "Preservation: Hello World!");
    app.MapControllers();
    app.MapHealthChecks("/health");
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
