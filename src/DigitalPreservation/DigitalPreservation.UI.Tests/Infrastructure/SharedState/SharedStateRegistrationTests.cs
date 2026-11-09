using System.Text;
using Amazon.DynamoDBv2;
using DigitalPreservation.UI.Infrastructure.SharedState;
using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.UI.Tests.Infrastructure.SharedState;

/// <summary>Issue #320: what <c>AddSharedState</c> wires up for each configuration.</summary>
public class SharedStateRegistrationTests
{
    private const string Prefix = "dlip-pres-test/preservation-ui/data-protection/";

    private static IServiceProvider Task(Dictionary<string, string?> settings, FakeSecretsManager? secrets = null,
        IAmazonDynamoDB? dynamo = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSharedState(configuration);
        if (secrets != null)
        {
            services.AddSingleton(secrets.Client);
        }
        if (dynamo != null)
        {
            services.AddSingleton(dynamo);
        }
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Aws(string? prefix = Prefix, string? table = "dlip-pres-test-ui-cache") => new()
    {
        ["SharedState:Store"] = "Aws",
        ["SharedState:TimeoutMilliseconds"] = "500",
        ["SharedState:Aws:KeySecretPrefix"] = prefix,
        ["SharedState:Aws:CacheTableName"] = table
    };

    private static IAmazonDynamoDB BrokenDynamo()
    {
        var dynamo = A.Fake<IAmazonDynamoDB>();
        A.CallTo(dynamo).Throws(new AmazonDynamoDBException("DynamoDB is down"));
        return dynamo;
    }

    [Fact]
    public void Without_configuration_state_stays_in_memory_as_before()
    {
        var sp = Task([]);

        sp.GetRequiredService<IDistributedCache>().Should().BeOfType<MemoryDistributedCache>();
    }

    [Fact]
    public void A_cookie_one_task_protects_another_task_can_read()
    {
        var secrets = new FakeSecretsManager();
        var first = Task(Aws(), secrets, BrokenDynamo());
        var second = Task(Aws(), secrets, BrokenDynamo());

        var cookie = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie").Protect("signed in");

        second.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie").Unprotect(cookie)
            .Should().Be("signed in");
        secrets.Secrets.Keys.Should().ContainSingle().Which.Should().StartWith(Prefix);
    }

    [Fact]
    public async Task With_every_AWS_service_down_the_UI_works_as_before()
    {
        var dynamo = BrokenDynamo();
        var sp = Task(Aws(), new FakeSecretsManager { Down = true }, dynamo);

        var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie");
        protector.Unprotect(protector.Protect("signed in")).Should().Be("signed in");

        var cache = sp.GetRequiredService<IDistributedCache>();
        cache.Should().BeOfType<FallbackDistributedCache>();
        await cache.SetAsync("k", Encoding.UTF8.GetBytes("v"), new DistributedCacheEntryOptions());
        (await cache.GetAsync("k")).Should().Equal(Encoding.UTF8.GetBytes("v"));
        // AWS's cache provider really is behind the wrapper, using the client registered for it.
        A.CallTo(dynamo).MustHaveHappened();
    }

    [Fact]
    public void Aws_with_its_names_missing_falls_back_to_memory()
    {
        var sp = Task(Aws(prefix: null, table: null));

        sp.GetRequiredService<IDistributedCache>().Should().BeOfType<MemoryDistributedCache>();
        var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie");
        protector.Unprotect(protector.Protect("signed in")).Should().Be("signed in");
    }
}
