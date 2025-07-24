using Amazon.SimpleNotificationService;
using Amazon.SQS;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using MediatR;
using MediatR.Registration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using Pipeline.API;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Pipeline.API.Features.Pipeline.Requests;
using Pipeline.API.Middleware;
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
            .Enrich.FromLogContext());

    builder.Services.Configure<ApiKeyOptions>(
        builder.Configuration.GetSection("ApiKeyOptions"));

    builder.Services.Configure<StorageOptions>(
        builder.Configuration.GetSection("StorageOptions"));

    builder.Services.Configure<BrunnhildeOptions>(
        builder.Configuration.GetSection("BrunnhildeOptions"));

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

    //Auth enabled flag
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    var useLocalHostedServiceForPipeline = builder.Configuration.GetValue<bool>("FeatureFlags:UseLocalHostedServiceForPipeline");


    //TODO: Use an API key to replace below


    builder.Services
        .AddMemoryCache()
        .AddPipeline(builder.Configuration)
        .AddControllers(config =>
        {
            if (useAuthFeatureFlag)
            {
                config.Filters.Add(new AuthorizeFilter());
                config.Filters.Add(new AuthFilterIdentifier());
            }
        });

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


    if (useLocalHostedServiceForPipeline)
    {
        builder.Services
            .AddHostedService<PipelineJobExecutorService>() 
            .AddScoped<PipelineJobRunner>()
            .AddSingleton<IPipelineQueue, InProcessPipelineQueue>();
    }
    else
    {
        builder.Services.AddSingleton<IPipelineQueue, SqsPipelineQueue>();
    }

    var app = builder.Build();
    app
        .UseMiddleware<ApiKeyMiddleware>()
        .UseSerilogRequestLogging()
        .UseRouting()
        .UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pipeline API"); // Adjust the endpoint path and name as needed
        });
    }

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
public partial class Program { }