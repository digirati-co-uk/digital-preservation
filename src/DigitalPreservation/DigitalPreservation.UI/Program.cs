using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.UI.Infrastructure;
using Preservation.Client;
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
            .Enrich.WithCorrelationId());

    // Add services to the container.
    builder.Services
        .AddHttpContextAccessor()
        .AddPreservationClient(builder.Configuration, "DigitalPreservation UI")
        .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>())
        .AddCorrelationIdHeaderPropagation()
        .AddUIHealthChecks()
        .AddRazorPages();

    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app
        .UseHttpsRedirection()
        .UseStaticFiles()
        .UseRouting()
        .UseAuthorization();
    app.MapRazorPages();
    app.UseHealthChecks("/health");
    app.Run();
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