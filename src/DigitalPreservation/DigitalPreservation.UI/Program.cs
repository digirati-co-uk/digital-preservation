using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.UI.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Workspace;
using LeedsDlipServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Preservation.Client;
using Serilog;
using Storage.Repository.Common;
using Storage.Repository.Common.S3;
using Microsoft.Identity.Web.UI;
using Storage.Repository.Common.Mets;



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


    IEnumerable<string>? initialScopes = new List<string>();
    builder.Configuration.GetSection("DownstreamApi:Scopes").Bind(initialScopes);

    builder.Services
        .AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();

    builder.Services.AddAuthentication()
        .AddMicrosoftIdentityWebApp(builder.Configuration, "AzureAd", Microsoft.Identity.Web.Constants.AzureAd,
            null)
        .EnableTokenAcquisitionToCallDownstreamApi(initialScopes)
        .AddSessionTokenCaches();

    // <ms_docref_add_default_controller_for_sign-in-out>
    builder.Services.AddRazorPages().AddMvcOptions(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
        options.Filters.Add(typeof(SessionTimeoutAsyncPageFilter));
    }).AddMicrosoftIdentityUI();

    // Add session timeout page filter
    builder.Services.AddSingleton<SessionTimeoutAsyncPageFilter>();
    builder.Services.AddSession();


    //Add TokenScope
    builder.Services.AddSingleton<ITokenScope>(x =>
        new TokenScope(builder.Configuration.GetSection("AzureAd:ScopeUri").Value));


    // Add services to the container.
    builder.Services
        .AddHttpContextAccessor()
        .ConfigureForwardedHeaders()
        .AddPreservationClient(builder.Configuration, "DigitalPreservation UI")
        .AddIdentityServiceClient(builder.Configuration)
        .AddMvpCatalogueClient(builder.Configuration)
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
            cfg.RegisterServicesFromAssemblyContaining<WorkspaceManagerFactory>();
        })
        .AddStorageAwsAccess(builder.Configuration)
        .AddSingleton<IMetsParser, MetsParser>()
        .AddSingleton<IMetsManager, MetsManager>()
        .AddSingleton<WorkspaceManagerFactory>()
        .AddCorrelationIdHeaderPropagation()
        .AddUIHealthChecks();


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
    app.UseSession();
    app.MapRazorPages();
    app.MapControllers();
    app.UseHealthChecks("/health");
    app.UseAuthentication();




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