using Amazon.S3;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using Microsoft.OpenApi.Models;
using Pipeline.API;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Pipeline.API.Middleware;
using Serilog;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Workspace;
using Preservation.Client;
using Storage.Repository.Common.Mets;
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
            .Enrich.FromLogContext());

    builder.Services.Configure<ApiKeyOptions>(
        builder.Configuration.GetSection("ApiKeyOptions"));

    builder.Services.Configure<StorageOptions>(
        builder.Configuration.GetSection("StorageOptions"));

    builder.Services.Configure<BrunnhildeOptions>(
        builder.Configuration.GetSection("BrunnhildeOptions"));

    builder.Services.Configure<PipelineOptions>(
        builder.Configuration.GetSection("PipelineOptions"));

    //Add TokenScope
    builder.Services.AddSingleton<ITokenScope>(x =>
        new TokenScope(builder.Configuration.GetSection("AzureAd:ScopeUri").Value));

    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IAmazonSimpleNotificationService>();
            cfg.RegisterServicesFromAssemblyContaining<IStorage>();
            cfg.RegisterServicesFromAssemblyContaining<WorkspaceManagerFactory>();
        })
        .AddMachinePreservationClient(builder.Configuration, "PipelineAPI" );

    builder.Services
        .AddAWSService<IAmazonSQS>()
        .AddAWSService<IAmazonSimpleNotificationService>();

    builder.Services
        .AddMemoryCache()
        .AddControllers();

    builder.Services.AddHealthChecks();

    builder.Services.AddAuthentication(options =>
    {
        // Authorization and authentication are closely linked in ASP.NET Core.
        // Ref: https://stackoverflow.com/questions/51142845/no-authenticationscheme-was-specified-and-there-was-no-defaultforbidscheme-foun
        options.DefaultChallengeScheme = "LeedsScheme";
        options.DefaultForbidScheme = "LeedsScheme";
    });

    builder.Services.AddTransient<ApiKeyMiddleware>();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Pipeline API", Version = "v1" });
        c.AddSecurityDefinition("ApiKeyAuth", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-API-KEY",
            Description = "Key auth scheme"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyAuth" }
                },
                []
            }
        });
    });


    var accessTokenProviderOptions = new AccessTokenProviderOptions();
    builder.Configuration.GetSection("TokenProvider").Bind(accessTokenProviderOptions);
    builder.Services.AddSingleton<IAccessTokenProviderOptions>(accessTokenProviderOptions);
    builder.Services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();

    builder.Services
        .AddHostedService<PipelineJobExecutorService>()
        .AddScoped<PipelineJobRunner>()
        .AddSingleton<IPipelineQueue, InProcessPipelineQueue>()
        .AddSingleton<IPipelineQueue, SqsPipelineQueue>();

    builder.Services.AddSingleton<IIdentityMinter, IdentityMinter>();
    builder.Services.AddAWSService<IAmazonS3>();

    builder.Services.AddStorageAwsAccess(builder.Configuration);
    builder.Services.AddSingleton<IMetsParser, MetsParser>();
    builder.Services.AddSingleton<IMetsManager, MetsManager>();
    builder.Services.AddSingleton<IMetsStorage, MetsStorage>();
    builder.Services.AddSingleton<WorkspaceManagerFactory>();

    var app = builder.Build();
    app
        .UseMiddleware<ApiKeyMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pipeline API");
    });

    // TODO - remove this, only used for initial setup
    app.MapGet("/", () => "Pipeline API: Hello World!");
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
public partial class Program
{
}