using DigitalPreservation.CommonApiClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Preservation.Client;

namespace Preservation.API.Tests.DependencyInjection;

/// <summary>
/// ITokenScope used to be registered by hand at each of three call sites - the UI; and Pipeline.API
/// and the Archiver, both dead, since they call AddMachinePreservationClient instead, which never
/// resolves it (issue #388 item 1.4). AddDownstreamPreservationClient now registers it itself, since
/// AuthTokenInjector - its only real consumer - is registered by this same method. These pin that
/// the registration now lives here, and that the dead-registration diagnosis was correct.
/// </summary>
public class PreservationClientServiceCollectionXTests
{
    private static IConfiguration BuildConfiguration(string? scopeUri) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Preservation:Root"] = "https://preservation.test/",
                ["AzureAd:ScopeUri"] = scopeUri
            })
            .Build();

    [Fact]
    public void AddDownstreamPreservationClient_Registers_ITokenScope_From_AzureAd_ScopeUri()
    {
        var services = new ServiceCollection();
        services.AddDownstreamPreservationClient(BuildConfiguration("api://preservation/.default"), "test-component");
        using var provider = services.BuildServiceProvider();

        var tokenScope = provider.GetRequiredService<ITokenScope>();

        tokenScope.ScopeUri.Should().Be("api://preservation/.default");
    }

    [Fact]
    public void AddMachinePreservationClient_Does_Not_Register_ITokenScope()
    {
        // Confirms the dead registrations removed from Pipeline.API's Program.cs and the
        // Archiver's ArchiverServiceCollectionX.cs were genuinely dead: this method never resolved
        // ITokenScope even back when it was registered by hand alongside it.
        var services = new ServiceCollection();
        services.AddMachinePreservationClient(BuildConfiguration(null), "test-component");
        using var provider = services.BuildServiceProvider();

        provider.GetService<ITokenScope>().Should().BeNull();
    }
}
