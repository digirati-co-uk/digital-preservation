using Amazon.SimpleNotificationService;
using Amazon.SQS;
using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using MediatR;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Pipeline.API;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Pipeline.API.Middleware;
using Serilog;
using Pipeline.API.ApiClients;
using Pipeline.API.Features;

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

    //Add TokenScope
    builder.Services.AddSingleton<ITokenScope>(x =>
        new TokenScope(builder.Configuration.GetSection("AzureAd:ScopeUri").Value));

    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.RegisterServicesFromAssemblyContaining<IRequest>();
            cfg.RegisterServicesFromAssemblyContaining<IAmazonSQS>();
            cfg.RegisterServicesFromAssemblyContaining<IAmazonSimpleNotificationService>();
        });


    builder.Services
        .AddMemoryCache()
        .AddPipeline(builder.Configuration)
        .AddControllers();

    builder.Services.AddHealthChecks();

    builder.Services.AddAuthentication(options =>
    {
        // Authorization and authentication are closely linked in ASP.NET Core.
        // Ref: https://stackoverflow.com/questions/51142845/no-authenticationscheme-was-specified-and-there-was-no-defaultforbidscheme-foun
        options.DefaultChallengeScheme = "LeedsScheme";
        options.DefaultForbidScheme = "LeedsScheme";
        options.AddScheme<LeedsSchemeHandler>("LeedsScheme", "Leeds's Pipeline API Challenge Scheme");
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

    builder.Services.AddHttpClient("PreservationApi", (provider, httpClient) =>
    {
        httpClient.BaseAddress = new Uri(provider.GetService<IConfiguration>().GetConnectionString("PreservationApiBaseUrl"));
        httpClient.DefaultRequestHeaders.Add("x-client-identity", "PipelineApi");
    });


    builder.Services
        .AddHostedService<PipelineJobExecutorService>()
        .AddScoped<PipelineJobRunner>()
        .AddSingleton<IPipelineQueue, InProcessPipelineQueue>()
        .AddSingleton<IPipelineQueue, SqsPipelineQueue>();

    builder.Services.AddSingleton<IPipelineJobStateLogger, PipelineJobStateLogger>();
    builder.Services.AddSingleton<IPreservationApiInterface, PreservationApiInterface>();

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

static void SetupOptions(IConfiguration configuration,
    DbContextOptionsBuilder optionsBuilder)
    => optionsBuilder
        .UseNpgsql(configuration.GetConnectionString("Postgres")) //ConnectionStringKey
        .UseSnakeCaseNamingConvention();

// required for WebApplicationFactory
public partial class Program
{
}