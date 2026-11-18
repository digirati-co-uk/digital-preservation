using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Storage.API.Tests.TestingInfrastructure;

/// <summary>
/// A real Fedora, the same image and digest as dev, test and production (preservation-ops
/// variables.tf), for tests that need to know what Fedora actually does with a request rather
/// than what text the platform sends it. Properties management is relaxed, as in the deployed
/// environments, so fedora:lastModifiedBy can be written.
/// </summary>
public class FedoraFixture : IAsyncLifetime
{
    public const string Image = "fcrepo/fcrepo:6.5.1-tomcat9@sha256:7f8295177edac4eb5d4599a371335d7cab647bf2309aa7528734b6c13990c19e";
    private const int FedoraPort = 8080;
    private const string User = "fedoraAdmin";

    private readonly IContainer fedoraContainer = new ContainerBuilder(Image)
        .WithEnvironment("CATALINA_OPTS", "-Dfcrepo.properties.management=relaxed")
        .WithPortBinding(FedoraPort, true)
        .WithCleanUp(true)
        .WithLabel("digitalpreservation_test", "True")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(FedoraPort)
            .ForPath("/fcrepo/rest/")
            .WithBasicAuthentication(User, User)))
        .Build();

    /// <summary>The repository root, with a trailing slash.</summary>
    public Uri Root { get; private set; } = null!;

    public HttpClient Client { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await fedoraContainer.StartAsync();
        Root = new Uri($"http://{fedoraContainer.Hostname}:{fedoraContainer.GetMappedPublicPort(FedoraPort)}/fcrepo/rest/");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{User}:{User}")));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await fedoraContainer.StopAsync();
    }
}

[CollectionDefinition(CollectionName)]
public class FedoraCollection : ICollectionFixture<FedoraFixture>
{
    public const string CollectionName = "Fedora Collection";
}
