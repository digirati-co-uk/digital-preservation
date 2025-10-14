using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.UI.Infrastructure;
using DigitalPreservation.Workspace;
using LeedsDlipServices;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Preservation.Client;
using Serilog;
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

    // Don't impose any limit for file uploads
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = long.MaxValue);

    IEnumerable<string>? initialScopes = new List<string>();
    builder.Configuration.GetSection("DownstreamApi:Scopes").Bind(initialScopes);

    builder.Services.AddDistributedMemoryCache();

    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi(initialScopes)
        .AddDistributedTokenCaches();

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = options.DefaultPolicy;
    });


    builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options => options.Events = new RejectSessionCookieWhenAccountNotInCacheEvents());
    builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection("PipelineOptions"));
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
    builder.Services.AddTransient<SessionTimeoutAsyncPageFilter>();
    builder.Services.AddSession();

    //Add TokenScope
    builder.Services.AddSingleton<ITokenScope>(x =>
        new TokenScope(builder.Configuration.GetSection("AzureAd:ScopeUri").Value));


    // Add services to the container.
    builder.Services
        .AddHttpContextAccessor()
        .ConfigureForwardedHeaders()
        .AddDownstreamPreservationClient(builder.Configuration, "DigitalPreservation UI")
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
        //app.UseHsts();
    }
    app.UseHsts();
    
    app
        .UseHttpsRedirection()
        .UseStaticFiles()
        .UseRouting()
        .UseForwardedHeaders();
    
    app.UseSession();
    app.MapRazorPages();
    app.MapControllers();
    app.UseHealthChecks("/health");
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