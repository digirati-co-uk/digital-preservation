using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.UI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Identity.Web;
using Preservation.Client;
using Serilog;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;
using Microsoft.Identity.Web.UI;

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


    // <ms_docref_add_msal>
    IEnumerable<string>? initialScopes = builder.Configuration["DownstreamApi:Scopes"]?.Split(' ');

    //Add Authentication
    builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi(initialScopes)
        //.AddDownstreamApi("DownstreamApi", builder.Configuration.GetSection("DownstreamApi"))
        .AddInMemoryTokenCaches();


    // <ms_docref_add_default_controller_for_sign-in-out>
    builder.Services.AddRazorPages().AddMvcOptions(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
    }).AddMicrosoftIdentityUI();

    // Add services to the container.
    builder.Services
        .AddHttpContextAccessor()
        .ConfigureForwardedHeaders()
        .AddPreservationClient(builder.Configuration, "DigitalPreservation UI")
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
        })
        .AddStorageAwsAccess(builder.Configuration)
        .AddCorrelationIdHeaderPropagation()
        .AddUIHealthChecks()
        .AddRazorPages();

    builder.Services.AddControllers();

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
        .UseAuthorization()
        .UseForwardedHeaders();
    app.MapRazorPages();
    app.MapControllers();
    app.UseHealthChecks("/health");
    //Authentication
    app.UseAuthentication();
    app.UseAuthorization();
   
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