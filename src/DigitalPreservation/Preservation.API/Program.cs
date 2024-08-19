using Serilog;

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
    
    builder.Services.AddHttpContextAccessor();
    
    var app = builder.Build();
    app
        .UseSerilogRequestLogging()
        .UseForwardedHeaders();
    
    app.MapGet("/", () => "Preservation: Hello World!");

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
