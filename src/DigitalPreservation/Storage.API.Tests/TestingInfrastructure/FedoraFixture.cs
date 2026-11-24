using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Storage.API.Fedora;
using Storage.API.Fedora.Model;
using Storage.Repository.Common;
using Testcontainers.PostgreSql;

namespace Storage.API.Tests.TestingInfrastructure;

/// <summary>
/// A real Fedora on its own PostgreSQL, for tests that need to know what Fedora actually does with
/// a request rather than what text the platform sends it. One subclass per Fedora image, so the
/// same contract tests run against the version we deploy and the version we're evaluating
/// (RFC 010 T14, ULIIIFCSS-45). Properties management is relaxed, as in the deployed environments,
/// so fedora:lastModifiedBy can be written.
/// </summary>
/// <remarks>
/// PostgreSQL, not Fedora's embedded H2: the Storage API reads Fedora's own tables for search and
/// for wide containers (FedoraDB), and Fedora 7 migrates that schema at start-up, so both need
/// testing against the database we run. Storage is Fedora's local OCFL, not S3: commit-time S3
/// behaviour (RFC 010 section 2.7) needs a deployed environment.
/// </remarks>
public abstract class FedoraFixture : IAsyncLifetime
{
    private const int FedoraPort = 8080;
    private const string User = "fedoraAdmin";
    private const string DatabaseAlias = "fedora-db";
    private const string DatabaseName = "fcrepo";
    private const string DatabaseUser = "fcrepo";
    private const string DatabasePassword = "fcrepo_pword";

    /// <summary>
    /// fcrepo.session.timeout, shortened from its 3-minute default so the expiry tests don't take
    /// minutes. Every other test finishes well inside it.
    /// </summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(20);

    private readonly INetwork network;
    private readonly PostgreSqlContainer database;
    private readonly IContainer fedoraContainer;

    protected FedoraFixture(string image)
    {
        Image = image;
        network = new NetworkBuilder()
            .WithName("fedora-contract-" + Guid.NewGuid().ToString("N")[..12])
            .WithCleanUp(true)
            .Build();

        database = new PostgreSqlBuilder("postgres:14-alpine")
            .WithNetwork(network)
            .WithNetworkAliases(DatabaseAlias)
            .WithDatabase(DatabaseName)
            .WithUsername(DatabaseUser)
            .WithPassword(DatabasePassword)
            .WithCleanUp(true)
            .WithLabel("digitalpreservation_test", "True")
            .Build();

        // As the deployed environments set them (preservation-ops modules/fedora/fargate/main.tf),
        // where they affect what Fedora answers. Without the digest algorithm, Fedora also stores
        // its own sha512, hasMessageDigest becomes an array, and FedoraClient can't read a binary.
        string[] properties =
        [
            "-Dfcrepo.properties.management=relaxed",
            "-Dfcrepo.persistence.defaultDigestAlgorithm=sha256",
            "-Dfcrepo.jms.enabled=false",
            $"-Dfcrepo.db.url=jdbc:postgresql://{DatabaseAlias}:5432/{DatabaseName}",
            $"-Dfcrepo.db.user={DatabaseUser}",
            $"-Dfcrepo.db.password={DatabasePassword}",
            $"-Dfcrepo.session.timeout={(int)SessionTimeout.TotalMilliseconds}"
        ];
        fedoraContainer = new ContainerBuilder(image)
            .WithNetwork(network)
            .WithEnvironment("CATALINA_OPTS", string.Join(' ', properties))
            .WithPortBinding(FedoraPort, true)
            .WithCleanUp(true)
            .WithLabel("digitalpreservation_test", "True")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(FedoraPort)
                .ForPath("/fcrepo/rest/")
                .WithBasicAuthentication(User, User)))
            .Build();
    }

    /// <summary>The Docker image, with its digest.</summary>
    public string Image { get; }

    /// <summary>The repository root, with a trailing slash.</summary>
    public Uri Root { get; private set; } = null!;

    /// <summary>For raw requests: authenticated, no base address.</summary>
    public HttpClient Client { get; } = new();

    /// <summary>Fedora's own database, as the Storage API's FedoraDB reads it.</summary>
    public string DatabaseConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await network.CreateAsync();
        await database.StartAsync();
        await fedoraContainer.StartAsync();
        Root = new Uri($"http://{fedoraContainer.Hostname}:{fedoraContainer.GetMappedPublicPort(FedoraPort)}/fcrepo/rest/");
        Client.DefaultRequestHeaders.Authorization = BasicAuth();
        DatabaseConnectionString = database.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await fedoraContainer.StopAsync();
        await database.StopAsync();
        await network.DeleteAsync();
    }

    /// <summary>
    /// The Storage API's own client, pointed at this Fedora. <paramref name="storage"/> supplies
    /// binary content; <paramref name="withDatabase"/> lets it read Fedora's tables. FedoraDB looks up
    /// RDF type ids when it's made, and finds our Archival Group type only once one exists, so make a
    /// database-backed client after creating one.
    /// </summary>
    internal FedoraClient CreateClient(IStorage? storage = null, bool withDatabase = false)
    {
        var fedoraOptions = FedoraOptions();
        var converters = CreateConverters();
        var fedoraDb = withDatabase ? CreateFedoraDb() : new FedoraDB(converters, null, NullLogger<FedoraDB>.Instance);
        var httpClient = new HttpClient { BaseAddress = Root };
        httpClient.DefaultRequestHeaders.Authorization = BasicAuth();

        return new FedoraClient(
            httpClient,
            storage ?? FakeItEasy.A.Fake<IStorage>(),
            NullLogger<FedoraClient>.Instance,
            fedoraOptions,
            converters,
            new MemoryCache(new MemoryCacheOptions()),
            FakeItEasy.A.Fake<IStorageMapper>(),
            fedoraDb);
    }

    /// <summary>Runs a command in the Fedora container, e.g. to read an OCFL inventory.</summary>
    public async Task<string> Exec(params string[] command) =>
        (await fedoraContainer.ExecAsync(command)).Stdout;

    /// <summary>The Storage API's reader of Fedora's own tables, as the client uses it.</summary>
    internal FedoraDB CreateFedoraDb() =>
        new(CreateConverters(), DatabaseConnectionString, NullLogger<FedoraDB>.Instance);

    internal Converters CreateConverters() =>
        new(FedoraOptions(), Options.Create(new ConverterOptions { StorageRoot = StorageRoot }));

    private IOptions<FedoraOptions> FedoraOptions() => Options.Create(new FedoraOptions
    {
        Root = Root,
        AdminUser = User,
        AdminPassword = User,
        Bucket = "fedora-bucket",
        OcflS3Prefix = string.Empty
    });

    /// <summary>Where the client's resources say they live: the Storage API's /repository.</summary>
    public static readonly Uri StorageRoot = new("https://storage.test/");

    private static AuthenticationHeaderValue BasicAuth() =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{User}:{User}")));
}

/// <summary>The Fedora every environment runs (preservation-ops variables.tf).</summary>
public sealed class Fedora651Fixture() : FedoraFixture(Image651)
{
    public const string Image651 =
        "fcrepo/fcrepo:6.5.1-tomcat9@sha256:7f8295177edac4eb5d4599a371335d7cab647bf2309aa7528734b6c13990c19e";
}

/// <summary>The upgrade candidate (RFC 010 section 7, ULIIIFCSS-45).</summary>
public sealed class Fedora700Fixture() : FedoraFixture(Image700)
{
    public const string Image700 =
        "fcrepo/fcrepo:7.0.0-tomcat10@sha256:9f52cfa0ef2dc0f3171e4df9d34d7f12b6352af293fc4f85f9aef34900188b62";
}

[CollectionDefinition(Name)]
public class Fedora651Collection : ICollectionFixture<Fedora651Fixture>
{
    public const string Name = "Fedora 6.5.1";
}

/// <summary>
/// Fedora 7.0.0. Its tests are Category=Manual, so CI leaves them out, until FedoraClient reads
/// Fedora 7's JSON-LD: 7.0.0 compacts with prefixes only ("premis:hasMessageDigest": {"@id": ...},
/// "ldp:contains") where 6.5.1 used term definitions ("hasMessageDigest": "...", "contains"), even
/// with the compacted profile we ask for. Run them with
/// <c>dotnet test --project Storage.API.Tests --filter-class "*700Tests"</c>.
/// </summary>
[CollectionDefinition(Name)]
public class Fedora700Collection : ICollectionFixture<Fedora700Fixture>
{
    public const string Name = "Fedora 7.0.0";
}
