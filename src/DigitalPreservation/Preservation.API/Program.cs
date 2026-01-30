using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Workspace;
using Preservation.API.Data;
using Preservation.API.Features.Activity.Readers;
using Preservation.API.Infrastructure;
using Preservation.API.Mutation;
using Serilog;
using Storage.Client;
using Storage.Repository.Common;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.S3;
using DigitalPreservation.Core.Auth;
using LeedsDlipServices;
using LeedsDlipServices.Identity;
using Microsoft.Extensions.DependencyInjection;
using Amazon.SimpleNotificationService;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using Microsoft.OpenApi.Models;
using Storage.Repository.Common.Mets.StorageImpl;


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

    builder.Services.Configure<PipelineOptions>(
        builder.Configuration.GetSection("PipelineOptions"));

    //Auth enabled flag
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");

    //Auth 
    if (useAuthFeatureFlag)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
    }

    builder.Services.AddAWSService<IAmazonSimpleNotificationService>();


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
        .AddIdentityServiceClient(builder.Configuration)
        .AddSingleton<IIdentityMinter, IdentityMinter>()
        .AddMvpCatalogueClient(builder.Configuration)
        .AddResourceMutator(builder.Configuration)
        .AddSingleton<IMetsLoader, S3MetsLoader>()
        .AddSingleton<IMetsParser, MetsParser>()
        .AddSingleton<IMetsManager, MetsManager>()
        .AddSingleton<IMetadataManager, MetadataManager>()
        .AddSingleton<IPremisManager<FileFormatMetadata>, PremisManager>()
        .AddSingleton<IPremisManager<ExifMetadata>, PremisManagerExif>()
        .AddSingleton<IPremisEventManager<VirusScanMetadata>, PremisEventManagerVirus>()
        .AddSingleton<IMetsStorage, S3MetsStorage>()
        .AddSingleton<WorkspaceManagerFactory>()
        .AddPreservationHealthChecks()
        .AddCorrelationIdHeaderPropagation()
        .AddPreservationContext(builder.Configuration)
        .AddControllers(config =>
        {
            if (useAuthFeatureFlag)
            {
                config.Filters.Add(new AuthorizeFilter());
                config.Filters.Add(new AuthFilterIdentifier());
            }
        });


    //Auth
    if (useAuthFeatureFlag)
    {
        var accessTokenProviderOptions = new AccessTokenProviderOptions();
        builder.Configuration.GetSection("TokenProvider").Bind(accessTokenProviderOptions);
        builder.Services.AddSingleton<IAccessTokenProviderOptions>(accessTokenProviderOptions);
        builder.Services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();
    }


    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Preservation API", Version = "v1" });

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


    builder.Services
        .AddHostedService<StorageImportJobsService>()
        .AddScoped<StorageImportJobsProcessor>();
    
    var app = builder.Build();
    app
        .UseMiddleware<CorrelationIdMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders()
        .TryRunMigrations(builder.Configuration, app.Logger);


    app.UseCors("AllowAll");

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