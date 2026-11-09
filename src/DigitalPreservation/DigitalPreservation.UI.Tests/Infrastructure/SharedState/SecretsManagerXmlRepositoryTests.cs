using System.Xml.Linq;
using DigitalPreservation.UI.Infrastructure.SharedState;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalPreservation.UI.Tests.Infrastructure.SharedState;

/// <summary>Issue #320: data-protection keys in Secrets Manager, and memory when it's unavailable.</summary>
public class SecretsManagerXmlRepositoryTests
{
    private const string Prefix = "dlip-pres-test/preservation-ui/data-protection/";

    private static SecretsManagerXmlRepository Repository(Func<Amazon.SecretsManager.IAmazonSecretsManager> client) =>
        new(client,
            Options.Create(new SharedStateOptions { Aws = { KeySecretPrefix = Prefix }, TimeoutMilliseconds = 500 }),
            NullLogger<SecretsManagerXmlRepository>.Instance);

    private static XElement Key(string id) => new("key", new XAttribute("id", id));

    [Fact]
    public void A_key_one_task_stores_is_read_by_another()
    {
        var sm = new FakeSecretsManager();
        Repository(() => sm.Client).StoreElement(Key("1"), "key-1");

        sm.Secrets.Should().ContainKey(Prefix + "key-1");
        Repository(() => sm.Client).GetAllElements().Should().ContainSingle()
            .Which.Attribute("id")!.Value.Should().Be("1");
    }

    [Fact]
    public void Secrets_outside_the_prefix_and_damaged_secrets_are_ignored()
    {
        var sm = new FakeSecretsManager();
        sm.Secrets[Prefix + "key-1"] = Key("1").ToString();
        sm.Secrets[Prefix.ToUpperInvariant() + "key-2"] = Key("2").ToString(); // matched by the filter, not the prefix
        sm.Secrets[Prefix + "key-3"] = "not xml";

        Repository(() => sm.Client).GetAllElements().Select(e => e.Attribute("id")!.Value)
            .Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public void While_Secrets_Manager_is_down_keys_are_kept_in_memory_without_errors()
    {
        var sm = new FakeSecretsManager { Down = true };
        var repository = Repository(() => sm.Client);

        repository.GetAllElements().Should().BeEmpty();
        var store = () => repository.StoreElement(Key("1"), "key-1");
        store.Should().NotThrow();

        repository.GetAllElements().Should().ContainSingle();
    }

    [Fact]
    public void A_key_made_during_an_outage_is_saved_when_Secrets_Manager_recovers()
    {
        var sm = new FakeSecretsManager { Down = true };
        var repository = Repository(() => sm.Client);
        repository.StoreElement(Key("1"), "key-1");

        sm.Down = false;
        repository.GetAllElements().Should().ContainSingle();

        sm.Secrets.Should().ContainKey(Prefix + "key-1");
        Repository(() => sm.Client).GetAllElements().Should().ContainSingle();
    }

    [Fact]
    public void Keys_already_read_survive_a_later_outage()
    {
        var sm = new FakeSecretsManager();
        sm.Secrets[Prefix + "key-1"] = Key("1").ToString();
        var repository = Repository(() => sm.Client);
        repository.GetAllElements().Should().ContainSingle();

        sm.Down = true;
        repository.GetAllElements().Should().ContainSingle();
    }

    [Fact]
    public void A_client_that_cannot_be_built_is_an_outage_not_an_error()
    {
        var repository = Repository(() => throw new InvalidOperationException("no credentials"));

        repository.GetAllElements().Should().BeEmpty();
        repository.StoreElement(Key("1"), "key-1");
        repository.GetAllElements().Should().ContainSingle();
    }

    [Fact]
    public void An_existing_secret_counts_as_saved()
    {
        var sm = new FakeSecretsManager();
        sm.Secrets[Prefix + "key-1"] = Key("1").ToString();
        var repository = Repository(() => sm.Client);

        repository.StoreElement(Key("1"), "key-1");

        sm.Down = true;
        repository.GetAllElements().Should().ContainSingle();
    }
}
