using DigitalPreservation.Core.Configuration;
using DigitalPreservation.Core.Web.Headers;
using Pipeline.API.Config;
using Pipeline.API.Features.Pipeline;
using Pipeline.API.Features.Pipeline.Requests;
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

    builder.Services.Configure<StorageOptions>(
        builder.Configuration.GetSection("StorageOptions"));

    builder.Services.Configure<BrunnhildeOptions>(
        builder.Configuration.GetSection("BrunnhildeOptions"));

    builder.Services
        .ConfigureForwardedHeaders()
        .AddHttpContextAccessor()
        .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ExecutePipelineJob>());

    builder.Services.AddHealthChecks();

    //Auth enabled flag - can still use this, eg for local testing scenarios
    var useAuthFeatureFlag = !builder.Configuration.GetValue<bool>("FeatureFlags:DisableAuth");
    if (useAuthFeatureFlag)
    {
        // Here we will need some auth config that allows Storage.API.Importer to have a ClientID and Secret
        // It _only_ makes calls this way because it never has a user context
    }

    builder.Services
        .AddMemoryCache()
        .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
        .AddPipeline(builder.Configuration);

    builder.Services
        .AddHostedService<PipelineJobExecutorService>() //add
        .AddScoped<PipelineJobRunner>()
        .AddSingleton<IPipelineQueue, SqsPipelineQueue>();
    
    var app = builder.Build();
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