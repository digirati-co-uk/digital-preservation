using DigitalPreservation.Core.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalPreservation.Core.Tests.Auth;

/// <summary>
/// Characterises how <see cref="ClientDirectoryServiceCollectionExtensions.AddClientDirectory"/>
/// binds the <c>KnownClients</c> section — in particular that malformed config fails at startup
/// instead of silently mis-resolving callers: the binder cannot enforce <c>ClientProfile</c>'s
/// <c>required Name</c>, and a flat <c>"appId": "name"</c> string entry would otherwise be dropped.
/// </summary>
public class ClientDirectoryConfigTests
{
    private const string Goobi = "22222222-2222-2222-2222-222222222222";

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private static IClientDirectory Register(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddClientDirectory(configuration);
        return services.BuildServiceProvider().GetRequiredService<IClientDirectory>();
    }

    [Fact]
    public void ObjectForm_BindsAndResolves()
    {
        var directory = Register(Config(
            ($"KnownClients:{Goobi}:name", "goobi"),
            ($"KnownClients:{Goobi}:depositBucket", "leeds-goobi-deposits")));

        directory.TryResolve(Goobi, out var profile).Should().BeTrue();
        profile!.Name.Should().Be("goobi");
        profile.DepositBucket.Should().Be("leeds-goobi-deposits");
    }

    [Fact]
    public void MissingSection_YieldsEmptyDirectory()
    {
        var directory = Register(Config(("Unrelated", "value")));

        directory.TryResolve(Goobi, out _).Should().BeFalse();
    }

    [Fact]
    public void FlatStringForm_FailsAtStartup()
    {
        // "appId": "name" is the shape an early RFC-0001 draft showed; it must be rejected loudly.
        var act = () => Register(Config(($"KnownClients:{Goobi}", "goobi")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{Goobi}*")
            .WithMessage("*objects*");
    }

    [Fact]
    public void ObjectWithoutName_FailsAtStartup()
    {
        var act = () => Register(Config(($"KnownClients:{Goobi}:depositBucket", "leeds-goobi-deposits")));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{Goobi}*");
    }
}
