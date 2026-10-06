using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Deposit.Archiver.Tests;

/// <summary>
/// The archiver's DI graph is only ever built in AWS, on a Lambda cold start - nothing else in CI
/// builds it (issue #321). A missing registration, like #318's missing IMemoryCache, otherwise
/// ships silently and fails on the first invocation after a deploy. This builds and validates the
/// real registrations (ArchiverServiceCollectionX.AddArchiverServices) with fake settings and no
/// network calls, then constructs Functions the way the generated Lambda entry point does.
/// </summary>
public class ArchiverServiceRegistrationTests
{
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:NormaliseMetsIdsOnWrite"] = "false",
                ["Storage-AWS:Region"] = "eu-west-1"
            })
            .Build();

    private static ArchiverStartupSettings BuildSettings() => new(
        ScopeUri: "api://fake/.default",
        ClientId: "fake-client-id",
        ClientSecret: "fake-client-secret",
        TenantId: "fake-tenant-id",
        ResourceUri: "https://fake-resource.example/",
        ClientBaseAddress: "https://preservation-api.example/");

    [Fact]
    public void The_Archiver_Lambdas_DI_Graph_Builds_And_Resolves_Functions()
    {
        // AWS SDK clients need a region to construct, even offline.
        Environment.SetEnvironmentVariable("AWS_REGION", "eu-west-1");

        var services = new ServiceCollection()
            .AddArchiverServices(BuildConfiguration(), BuildSettings());

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var scope = provider.CreateScope();

        // Constructed the way the Amazon.Lambda.Annotations source generator constructs it -
        // covers whatever Functions' constructor takes now or later, with no network calls.
        var act = () => ActivatorUtilities.CreateInstance<Functions>(scope.ServiceProvider);

        act.Should().NotThrow();
    }
}
