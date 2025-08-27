using Amazon.SimpleNotificationService;
using Amazon.SQS;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using Serilog;
using Storage.API.Data;
using Storage.API.Features.Export;
using Storage.API.Features.Export.Data;
using Storage.API.Features.Import;
using Storage.API.Features.Import.Data;
using Storage.API.Fedora;
using Storage.API.Infrastructure;
using Storage.API.Ocfl;
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

    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
        });

    //Auth enabled flag
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    var useLocalHostedServiceForImport = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForImport");
    var useLocalHostedServiceForExport = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForExport");


    if (useAuthFeatureFlag)
    {
        // Auth
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
    }


    builder.Services
        .AddMemoryCache()
        .AddOcfl(builder.Configuration)
        .AddFedoraClient(builder.Configuration, "Storage-API")
        .AddFedoraDB(builder.Configuration, "Fedora")
        .AddStorageAwsAccess(builder.Configuration)
        .AddImportExport(builder.Configuration)
        .AddSingleton<IIdentityMinter, IdentityMinter>()
        .AddScoped<IImportJobResultStore, ImportJobResultStore>() // only for Storage API; happens after above for shared S3
        .AddScoped<IExportResultStore, ExportResultStore>() // only for Storage API; happens after above for shared S3
        .AddStorageHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddStorageContext(builder.Configuration)
        .AddControllers(config =>
        {
            if (useAuthFeatureFlag)
            {
                config.Filters.Add(new AuthorizeFilter());
                config.Filters.Add(new AuthFilterIdentifier());
            }
        });


    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Storage API", Version = "v1" });

        if (useAuthFeatureFlag)
        {

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                Description = "Bearer token add:   'Bearer <your token>'  "
            });
            
            c.AddSecurityRequirement(new OpenApiSecurityRequirement()
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        },
                        Scheme = "oauth2",
                        Name = "Bearer",
                        In = ParameterLocation.Header,

                    },
                    new List<string>()
                }
            });
        }

        c.AddSecurityDefinition("X-Client-Identity", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Client-Identity",
            Description = "client identity header for machine to machine calls"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "X-Client-Identity"
                    }
                },
                []
            }
        });
    });


    if (useLocalHostedServiceForImport)
    {
        builder.Services
            .AddHostedService<ImportJobExecutorService>()
            .AddScoped<ImportJobRunner>()
            .AddSingleton<IImportJobQueue, InProcessImportJobQueue>(); // <= SqsExportQueue
    }
    else
    {
        // The Import Service is a separate ECR, a separate scalable service...
        builder.Services.AddSingleton<IImportJobQueue, SqsImportJobQueue>();
    }

    if (useLocalHostedServiceForExport)
    {
        // ...but export is much less used, and can run alongside the Storage API as
        // a Hosted Service
        builder.Services
            .AddHostedService<ExportExecutorService>()
            .AddScoped<ExportRunner>()
            .AddSingleton<IExportQueue, InProcessExportQueue>(); // <= SqsExportQueue
    }
    else
    {
        throw new NotSupportedException("Separate export service not yet implemented!");
    }
    
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);

    //Auth
    if (useAuthFeatureFlag)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Storage API");
    });

    // TODO - remove this, only used for initial setup
    app.MapGet("/", () => "Storage: Hello World!");
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