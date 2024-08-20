using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Test.Helpers;

public class DigitalPreservationAppFactory<TStartup> : WebApplicationFactory<TStartup> where TStartup : class
{
    private readonly Dictionary<string, string?> configuration = new();
    private Action<IServiceCollection>? configureTestServices;

    /// <summary>
    /// Specify connection string to use for dbContext when building services
    /// </summary>
    /// <param name="connectionString">connection string to use for dbContext - docker instance</param>
    /// <returns>Current instance</returns>
    public DigitalPreservationAppFactory<TStartup> WithConnectionString(string connectionString)
    {
        configuration["ConnectionStrings:Postgres"] = connectionString;
        return this;
    }
    
    /// <summary>
    /// Specify a configuration value to be set in appFactory
    /// </summary>
    /// <param name="key">Key of setting to update, in format ("foo:bar")</param>
    /// <param name="value">Value to set</param>
    /// <returns>Current instance</returns>
    public DigitalPreservationAppFactory<TStartup> WithConfigValue(string key, string value)
    {
        configuration[key] = value;
        return this;
    }
    
    /// <summary>
    /// Action to call in ConfigureTestServices
    /// </summary>
    /// <returns>Current instance</returns>
    public DigitalPreservationAppFactory<TStartup> WithTestServices(Action<IServiceCollection> configure)
    {
        this.configureTestServices = configure;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = Directory.GetCurrentDirectory();
        var configPath = Path.Combine(projectDir, "appsettings.Testing.json");

        builder
            .ConfigureAppConfiguration((context, conf) =>
            {
                conf.AddJsonFile(configPath, optional: true);
                conf.AddInMemoryCollection(configuration);
            })
            .ConfigureServices(services =>
            {
                configureTestServices?.Invoke(services);
            })
            .UseEnvironment("Testing")
            .UseDefaultServiceProvider((_, options) =>
            {
                options.ValidateScopes = true;
            });
    }
}